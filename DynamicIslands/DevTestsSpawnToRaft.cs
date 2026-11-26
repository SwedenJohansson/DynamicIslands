using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>Spawn to raft (PlayerMove.cs SpawnToRaft) in the world's settings (Esc > Custom Islands): a player in a pit, the box, Cancel, then Yes.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CISpawnToRaft", docs: "Dev, in game (either player): a pit 50 m off the raft, Esc > Custom Islands, Spawn to raft (beside Skip current quest step), Cancel, again, Yes - on the raft? Pictures shot_spawn_window.png, shot_spawn_box.png")]
		public static void SpawnToRaftCommand() { StartTest(SpawnToRaftRoutine()); }

		static IEnumerator SpawnToRaftRoutine()
		{
			bool ok = true;
			Network_Player p = RAPI.GetLocalPlayer();
			Vector3? middle = CustomIslandSpawner.RaftPosition;
			PauseMenu menu = Object.FindObjectOfType<PauseMenu>();
			if (p == null || !middle.HasValue || menu == null) { Fail("spawn to raft: no player, raft or pause menu"); yield break; }
			KeepAlive(p);

			// (the pit: a floor 3 m above the sea with walls 12 m high round a 1.6 m shaft - no way out)
			GameObject pit = new GameObject("CISpawnPit");
			Vector3 c = middle.Value + new Vector3(50f, 0f, 0f);
			c.y = 3f;
			System.Action<Vector3, Vector3> wall = (at, size) =>
			{
				GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
				g.transform.SetParent(pit.transform, false);
				g.transform.position = at;
				g.transform.localScale = size;
			};
			wall(c + Vector3.down * 0.5f, new Vector3(4f, 1f, 4f));
			wall(c + new Vector3(1.3f, 6f, 0f), new Vector3(1f, 12f, 4f));
			wall(c + new Vector3(-1.3f, 6f, 0f), new Vector3(1f, 12f, 4f));
			wall(c + new Vector3(0f, 6f, 1.3f), new Vector3(1.6f, 12f, 1f));
			wall(c + new Vector3(0f, 6f, -1.3f), new Vector3(1.6f, 12f, 1f));
			try
			{
				PlayerMove.To(p, c + Vector3.up * 1.2f);
				yield return new WaitForSecondsRealtime(2f);
				Check(ref ok, (p.transform.position - c).magnitude < 3f, "the player is in the pit (" + (p.transform.position - c).magnitude.ToString("F1") + " m from its floor)");

				// the window with the button, beside Skip current quest step
				CallPrivate(menu, "Pause");
				yield return new WaitForSecondsRealtime(0.6f);
				WorldWindow.Open();
				yield return new WaitForSecondsRealtime(1.2f);
				UnityEngine.UI.Button b = WorldWindow.ButtonNamed("SpawnToRaft"), skip = WorldWindow.ButtonNamed("SkipStep");
				Check(ref ok, b != null && b.gameObject.activeInHierarchy, "the world's settings have the Spawn to raft button");
				if (b == null) yield break;
				Check(ref ok, !menu.buttonHolderPanel.transform.Cast<Transform>().Any(t => t.gameObject.activeSelf && t.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(x => x.text.ToUpperInvariant().Contains("SPAWN TO RAFT"))), "the pause menu has no SPAWN TO RAFT button any more");
				Check(ref ok, skip != null && skip.transform.parent == b.transform.parent, "it is in the same row as Skip current quest step");
				string label = UIKit.LabelOf(b) != null ? UIKit.LabelOf(b).text : "";
				Check(ref ok, label == SpawnToRaft.Label, "its label: '" + label + "'");
				Check(ref ok, b.interactable, "it can be clicked (" + (SpawnToRaft.WhyNot() ?? "ready") + (Raft_Network.IsHost ? ", host" : ", client") + ")");
				if (!Raft_Network.IsHost)
				{
					Text note = WorldWindow.NoteText("SkipNote");
					Check(ref ok, skip != null && skip.gameObject.activeInHierarchy && !skip.interactable, "client: Skip current quest step shows, greyed");
					Check(ref ok, note != null && note.text.Contains("ask the host"), "client: the note says to ask the host ('" + (note != null ? note.text : "none") + "')");
				}
				else
				{
					// (20a648c1: the host's note names the two buttons, no F10 command any more)
					Text note = WorldWindow.NoteText("SkipNote");
					Check(ref ok, note != null && note.text.Contains("Spawn to raft") && !note.text.Contains("F10"), "host: the note names Skip and Spawn to raft, no F10 ('" + (note != null ? note.text : "none") + "')");
				}
				Screenshot(new[] { "spawn_window" });
				yield return new WaitForSecondsRealtime(0.8f);

				// the box, Cancel
				b.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.4f);
				Check(ref ok, QuestSkip.AskingSpawn, "the box asks (Spawn to raft)");
				Screenshot(new[] { "spawn_box" });
				yield return new WaitForSecondsRealtime(0.8f);
				if (QuestSkip.CancelButton != null) QuestSkip.CancelButton.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.5f);
				Check(ref ok, !QuestSkip.ConfirmShown && WorldWindow.IsOpen && CanvasHelper.ActiveMenu == MenuType.PauseMenu, "Cancel: the box is gone, the window and the menu stay");
				Check(ref ok, (p.transform.position - c).magnitude < 3f, "Cancel: the player is still in the pit");

				// again, Yes
				b.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.4f);
				Check(ref ok, QuestSkip.AskingSpawn, "a second click opens the box again");
				float health = p.Stats.stat_health.Value;
				if (QuestSkip.YesButton != null) QuestSkip.YesButton.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, !QuestSkip.ConfirmShown && !WorldWindow.IsOpen && CanvasHelper.ActiveMenu != MenuType.PauseMenu, "Yes: the box, the window and the menu are closed");
				yield return new WaitForSecondsRealtime(3f);
				Raft raft = ComponentManager<Raft>.Value;
				RaycastHit hit;
				bool onRaft = Physics.Raycast(p.transform.position, Vector3.down, out hit, 3f, ~(1 << LayerMask.NameToLayer("LocalPlayer")), QueryTriggerInteraction.Ignore)
					&& raft != null && (hit.collider.transform.IsChildOf(raft.transform)
						// (Raft's FoundationCollider stands in for foundation blocks and is not under the raft)
						|| hit.collider.name.Contains("FoundationCollider") || hit.collider.GetType().Name.Contains("FoundationCollider"));
				Check(ref ok, onRaft, "Yes: the player stands on the raft (" + (hit.collider != null ? hit.collider.name : "nothing") + " below, "
					+ (p.transform.position - CustomIslandSpawner.RaftPosition.Value).magnitude.ToString("F1") + " m from its middle, y " + p.transform.position.y.ToString("F1") + ")");
				Check(ref ok, p.PersonController.controllerType == ControllerType.Ground, "... walking, not swimming");
				Check(ref ok, p.Stats.stat_health.Value >= health - 0.5f, "... with no fall damage (health " + health.ToString("F0") + " -> " + p.Stats.stat_health.Value.ToString("F0") + ")");
			}
			finally { Object.Destroy(pit); QuestSkip.CloseConfirm(); WorldWindow.Close(); }
			if (ok) Log("PASS: spawn to raft"); else Fail("spawn to raft");
		}

		static void CallPrivate(object o, string method) { HarmonyLib.Traverse.Create(o).Method(method).GetValue(); }
	}
}
