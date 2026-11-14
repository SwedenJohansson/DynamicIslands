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
			// (w10fix3 11-14: also off Raft's "FirstPersonParent" - on The Listening Mirrors every reach walk started at sea a child
			// of it, grounded at -0.4 m over an 11 m deep sea and not moving; HasRaftAsParent knows only the raft's pivot)
			if (pc != null && (pc.HasRaftAsParent || IsFirstPersonParent(player.transform.parent))) player.transform.SetParent(null, true);
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

		/// <summary>Raft's scene object "FirstPersonParent" as the player's parent (see To).</summary>
		public static bool IsFirstPersonParent(Transform parent) { return parent != null && parent.name == "FirstPersonParent"; }

		/// <summary>How high above the ground Raft's player stands: their position over the bottom of their capsule, plus the
		/// skin and a hair. A player put with their position on the ground starts with the lower half of the capsule in it: on
		/// terrain they dropped through it and ended in the sea under the island (a test facing Mara Quill on Frontier Beacon).</summary>
		public static float StandHeight(Network_Player player)
		{
			CharacterController cc = player != null && player.PersonController != null ? player.PersonController.controller : null;
			if (cc == null) return 1f;
			Transform t = cc.transform;
			float bottom = t.TransformPoint(cc.center).y - Mathf.Max(cc.height * 0.5f, cc.radius) * Mathf.Abs(t.lossyScale.y);
			return Mathf.Max(0f, player.transform.position.y - bottom) + cc.skinWidth + 0.05f;
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
		static bool warned;

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
			try
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
			catch (System.Exception e)
			{
				// (Raft's own step runs then: the player is parented as Raft does it)
				if (!warned) { warned = true; Debug.LogWarning("[CUSTOM ISLANDS] Island ground under the player: " + e.Message); }
				return true;
			}
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

		static bool warned;

		static void Prefix(AttachPlayer __instance)
		{
			try
			{
				if (__instance.carriedPlayer == null) return;
				AI_NetworkBehaviour carrier = __instance.GetComponentInParent<AI_NetworkBehaviour>();
				if (carrier == null || carrier.networkEntity == null || !carrier.networkEntity.IsDead) return;
				__instance.StopCarryingPlayer(true, true);
				Released++;
				Debug.Log("[CUSTOM ISLANDS] A dead " + carrier.behaviourType + " let go of the player it carried");
			}
			catch (System.Exception e) { if (!warned) { warned = true; Debug.LogWarning("[CUSTOM ISLANDS] A dead carrier letting go: " + e.Message); } }
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
			static bool warned;

			static void Postfix(Unstuck_Tracker __instance)
			{
				try
				{
					if (!Local(__instance)) return;
					Transform tr = Tracked(__instance);
					if (tr == null) { since = -1f; return; }
					Vector3 at = tr.position;
					if (since < 0f || (at - anchor).sqrMagnitude > Radius * Radius) { anchor = at; since = Time.time; }
				}
				catch (System.Exception e) { if (!warned) { warned = true; Debug.LogWarning("[CUSTOM ISLANDS] Unstuck tracking: " + e.Message); } }
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(PauseMenu), "HandleToggleUnstuckPanel")]
		static class PanelPatch
		{
			static bool warned;

			static void Postfix(PauseMenu __instance)
			{
				try
				{
					Network_Player p = ComponentManager<Network_Player>.Value;
					if (p == null || p.UnstuckTracker == null || p.UnstuckTracker.IsConsideredStuck || !Stuck(p.UnstuckTracker)) return;
					GameObject panel = HarmonyLib.Traverse.Create(__instance).Field("unstuckPanel").GetValue<GameObject>();
					if (panel != null) panel.SetActive(true);
				}
				catch (System.Exception e) { if (!warned) { warned = true; Debug.LogWarning("[CUSTOM ISLANDS] Unstuck button: " + e.Message); } }
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(Unstuck_Tracker), "Unstuck")]
		static class UnstuckPatch
		{
			static bool Prefix(Unstuck_Tracker __instance)
			{
				try
				{
					if (!Stuck(__instance)) return true;
					PlayerHold.LetGo("used Unstuck: let go"); // (else held back in the air - UR2)
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

	/// <summary>
	/// A SPAWN TO RAFT button in Raft's pause menu (Esc), for a player stuck in a hole or a shaft they can't climb out of.
	/// It asks first, in a copy of Raft's own Unstuck yes/no box; Yes closes the menu and stands the local player on their
	/// raft on a walkable raft block as Raft's respawn without a bed does, here the first with room to stand,
	/// with no fall or push kept and the swimming state ended. Only the local player moves (a client too: each player's
	/// own position is theirs to send). Greyed while dead, asleep, on a zipline, or with no raft block to stand on.
	/// </summary>
	public static class SpawnToRaft
	{
		public const string ButtonName = "CustomIslands_SpawnToRaft", BoxName = "CustomIslands_SpawnToRaftBox";
		public const string Label = "SPAWN TO RAFT", Question = "Only use this if you have gotten stuck in a place you cannot get out of.";

		static PauseMenu menu;
		static GameObject box;
		static UnityEngine.UI.Button button, yesButton, noButton;

		/// <summary>Tests: the button, the box and its Yes/No, whether the box is up.</summary>
		public static UnityEngine.UI.Button Button { get { return button; } }
		public static UnityEngine.UI.Button YesButton { get { return yesButton; } }
		public static UnityEngine.UI.Button NoButton { get { return noButton; } }
		public static GameObject Box { get { return box; } }
		public static bool Asking { get { return box != null && box.activeSelf; } }
		/// <summary>Tests: log where a menu close with the box open came from.</summary>
		public static bool TraceClose;

		/// <summary>Null when the local player can be sent to the raft now, else why not.</summary>
		public static string WhyNot()
		{
			Network_Player p = ComponentManager<Network_Player>.Value;
			if (p == null || !LoadSceneManager.IsGameSceneLoaded) return "no player";
			if (Player.LocalPlayerIsDead || (p.PlayerScript != null && p.PlayerScript.IsDead)) return "dead";
			if (p.BedComponent != null && p.BedComponent.Sleeping) return "asleep";
			if (p.ZiplinePlayer != null && p.ZiplinePlayer.IsAttachedToZipline) return "on a zipline";
			Vector3 at;
			if (!Spot(p, out at)) return "no raft block";
			return null;
		}

		/// <summary>
		/// Where on the raft: on top of a walkable block of the raft (Raft's respawn takes the first one), the first with room
		/// to stand - nothing in a player-sized space above its floor. A client's list may hold no walkable blocks (CIOnRaft
		/// found Raft's own SetToWalkableBlockPosition did nothing there), so the raft's foundations count too, and last the
		/// deck found straight down from above the raft's middle.
		/// </summary>
		internal static bool Spot(Network_Player p, out Vector3 at)
		{
			at = Vector3.zero;
			Raft raft = ComponentManager<Raft>.Value;
			if (raft == null) return false;
			int mask = ~(1 << LayerMask.NameToLayer("LocalPlayer"));
			System.Collections.Generic.List<Block> blocks = BlockCreator.GetPlacedBlocks();
			if (blocks != null)
				for (int pass = 0; pass < 2; pass++)
					foreach (Block b in blocks)
					{
						if (b == null || !b.transform.IsChildOf(raft.transform) || b.GetComponentInParent<IslandInfoTag>() != null) continue;
						bool ok = pass == 0 ? b.IsWalkable() : b.buildableItem != null && b.buildableItem.UniqueName != null && b.buildableItem.UniqueName.IndexOf("Foundation", System.StringComparison.OrdinalIgnoreCase) >= 0;
						if (ok && Stand(b.transform.position + b.transform.up * 1.9f, raft, p, mask, out at)) return true;
					}
			Vector3? middle = CustomIslandSpawner.RaftPosition;
			return middle.HasValue && Stand(middle.Value + Vector3.up * 20f, raft, p, mask, out at, 40f);
		}

		/// <summary>Down from <paramref name="from"/> onto the raft: a floor of the raft with a player's room above it.</summary>
		static bool Stand(Vector3 from, Raft raft, Network_Player p, int mask, out Vector3 at, float reach = 2.6f)
		{
			at = Vector3.zero;
			RaycastHit hit;
			if (!Physics.Raycast(from, Vector3.down, out hit, reach, mask, QueryTriggerInteraction.Ignore) || hit.collider == null
				|| !hit.collider.transform.IsChildOf(raft.transform) || hit.normal.y < 0.7f) return false;
			foreach (Collider c in Physics.OverlapCapsule(hit.point + Vector3.up * 0.45f, hit.point + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore))
				if (c != null && !c.transform.IsChildOf(p.transform)) return false;
			at = hit.point + Vector3.up * 1.2f;
			return true;
		}

		/// <summary>Stands the local player on the raft (Yes in the box, and tests).</summary>
		public static bool Go()
		{
			try
			{
				string why = WhyNot();
				if (why != null) { Debug.Log("[CUSTOM ISLANDS] Spawn to raft: not now (" + why + ")"); return false; }
				Network_Player p = ComponentManager<Network_Player>.Value;
				Vector3 at;
				Spot(p, out at);
				PlayerHold.LetGo("chose Spawn to raft: let go"); // (else held back in the air - UR2)
				PlayerMove.To(p, at, ControllerType.Ground);
				if (p.UnstuckTracker != null) try { HarmonyLib.AccessTools.Method(typeof(Unstuck_Tracker), "SetIsStuck").Invoke(p.UnstuckTracker, new object[] { false }); } catch { }
				Debug.Log("[CUSTOM ISLANDS] Spawn to raft: the player stands on the raft at " + at.ToString("F1"));
				return true;
			}
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Spawn to raft: " + e.Message); return false; }
		}

		/// <summary>The button: the yes/no box over the menu, as Raft's Unstuck does it.</summary>
		public static void Ask()
		{
			try
			{
				if (menu == null || box == null || WhyNot() != null) return;
				box.SetActive(true);
				if (menu.buttonHolderPanel != null) menu.buttonHolderPanel.SetActive(false);
				Focus(box);
			}
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Spawn to raft box: " + e.Message); }
		}

		/// <summary>Yes or No in the box. Yes also closes the pause menu and sends the player.</summary>
		public static void Answer(bool yes)
		{
			try
			{
				Hide();
				if (!yes) return;
				if (menu != null) menu.ContinuePlaying();
				Go();
			}
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Spawn to raft: " + e.Message); }
		}

		static void Hide()
		{
			if (box == null || !box.activeSelf) return;
			box.SetActive(false);
			if (menu != null && menu.buttonHolderPanel != null) { menu.buttonHolderPanel.SetActive(true); Focus(menu.buttonHolderPanel); }
		}

		static void Focus(GameObject panel)
		{
			try
			{
				GamepadCursor cursor = HarmonyLib.Traverse.Create(menu).Field("cursor").GetValue<GamepadCursor>();
				UiMagnet magnet = panel.GetComponentInChildren<UiMagnet>();
				if (cursor != null && magnet != null) cursor.FocusMagnet(magnet);
			}
			catch { }
		}

		static void SetText(GameObject root, string text, bool skipButtons)
		{
			foreach (Component loc in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(root.GetComponentsInChildren<Component>(true),
				x => x != null && x.GetType().Name.IndexOf("Locali", System.StringComparison.OrdinalIgnoreCase) >= 0 && !(skipButtons && x.GetComponentInParent<UnityEngine.UI.Button>() != null))))
				Object.Destroy(loc);
			foreach (UnityEngine.UI.Text t in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
				if (!(skipButtons && t.GetComponentInParent<UnityEngine.UI.Button>() != null)) t.text = text;
			foreach (Component tmp in root.GetComponentsInChildren<Component>(true))
				if (tmp != null && tmp.GetType().Name.StartsWith("TextMeshPro") && !(skipButtons && tmp.GetComponentInParent<UnityEngine.UI.Button>() != null))
					HarmonyLib.Traverse.Create(tmp).Property("text").SetValue(text);
		}

		[HarmonyLib.HarmonyPatch(typeof(PauseMenu), "Start")]
		static class MenuPatch
		{
			static void Postfix(PauseMenu __instance)
			{
				try
				{
					menu = __instance;
					GameObject holder = __instance.buttonHolderPanel;
					GameObject unstuckBox = HarmonyLib.Traverse.Create(__instance).Field("yesNoPanelUnstuck").GetValue<GameObject>();
					if (holder == null || unstuckBox == null || holder.transform.Find(ButtonName) != null) return;
					UnityEngine.UI.Button[] own = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(holder.GetComponentsInChildren<UnityEngine.UI.Button>(true),
						b => b.transform.parent == holder.transform && !b.name.StartsWith("CustomIslands_")));
					if (own.Length == 0) return;

					// (the box: Raft's Unstuck question with our words; its Yes/No keep Raft's words and look)
					box = Object.Instantiate(unstuckBox, unstuckBox.transform.parent);
					box.name = BoxName;
					box.SetActive(false);
					SetText(box, Question, true);
					int wired = 0;
					foreach (UnityEngine.UI.Button b in box.GetComponentsInChildren<UnityEngine.UI.Button>(true))
					{
						string m = b.onClick.GetPersistentEventCount() > 0 ? b.onClick.GetPersistentMethodName(0) : "";
						bool yes = m.IndexOf("Yes", System.StringComparison.OrdinalIgnoreCase) >= 0;
						b.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
						if (yes) { b.onClick.AddListener(() => Answer(true)); yesButton = b; } else { b.onClick.AddListener(() => Answer(false)); noButton = b; }
						wired++;
					}

					GameObject copy = Object.Instantiate(own[0].gameObject, holder.transform);
					copy.name = ButtonName;
					SetText(copy, Label, false);
					button = copy.GetComponent<UnityEngine.UI.Button>();
					button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
					button.onClick.AddListener(Ask);
					copy.transform.SetSiblingIndex(Mathf.Max(0, own[own.Length - 1].transform.GetSiblingIndex()));
					Debug.Log("[CUSTOM ISLANDS] Pause menu: a Spawn to raft button (" + wired + " box buttons)");
				}
				catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] The pause menu's Spawn to raft button: " + e.Message); }
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(PauseMenu), "Update")]
		static class GreyPatch
		{
			static float next, noBlockSince = -1f;
			static string shown;

			static void Postfix(PauseMenu __instance)
			{
				try
				{
					if (button == null || __instance != menu) return;
					// (Esc or anything else closing the menu takes the box with it; nothing is checked while the menu is shut.
					//  Not pausePanel's state: the box hides the button holder, which made that panel read as shut)
					if (CanvasHelper.ActiveMenu != MenuType.PauseMenu) { Hide(); noBlockSince = -1f; return; }
					if (Time.unscaledTime < next) return;
					next = Time.unscaledTime + 0.25f;
					string why = WhyNot();
					// (RK16: the room check above a raft block can miss for a moment - something drifting by - and that closed the
					//  open question at once. "No raft block" greys the button only once it has lasted a second, and never closes
					//  the box: Yes checks again. Dead, asleep or on a zipline close it as before)
					bool noBlock = why == "no raft block";
					if (!noBlock) noBlockSince = -1f;
					else if (noBlockSince < 0f) noBlockSince = Time.unscaledTime;
					bool grey = why != null && (!noBlock || Time.unscaledTime - noBlockSince >= 1f);
					if (why != shown) { Debug.Log("[CUSTOM ISLANDS] Spawn to raft: " + (why == null ? "ready" : "not now (" + why + ")") + (Asking ? ", the box open" : "")); shown = why; }
					button.interactable = !grey;
					if (Asking && why != null && !noBlock) Hide();
				}
				catch { }
			}
		}

		[HarmonyLib.HarmonyPatch(typeof(PauseMenu), "MenuClose")]
		static class ClosePatch
		{
			static void Postfix()
			{
				try
				{
					if (Asking && TraceClose) Debug.Log("[CUSTOM ISLANDS] Spawn to raft: the menu closed with the box open, from:\n" + System.Environment.StackTrace);
					Hide();
				}
				catch { }
			}
		}
	}
}
