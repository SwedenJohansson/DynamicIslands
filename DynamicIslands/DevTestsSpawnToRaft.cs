using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>The pause menu's SPAWN TO RAFT button (PlayerMove.cs SpawnToRaft): a player in a pit, the box, No, then Yes.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CISpawnToRaft", docs: "Dev, in game (either player): a pit 50 m off the raft, Esc menu, SPAWN TO RAFT, No, again, Yes - on the raft? Picture shot_spawn_box.png")]
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

				// the menu with the button
				CallPrivate(menu, "Pause");
				yield return new WaitForSecondsRealtime(0.6f);
				UnityEngine.UI.Button b = SpawnToRaft.Button;
				Check(ref ok, b != null && b.gameObject.activeInHierarchy, "the pause menu has the SPAWN TO RAFT button");
				if (b == null) yield break;
				string label = string.Join(" ", b.GetComponentsInChildren<UnityEngine.UI.Text>(true).Select(t => t.text).ToArray());
				Check(ref ok, label.Contains(SpawnToRaft.Label), "its label: '" + label + "'");
				Check(ref ok, b.interactable, "it can be clicked (" + (SpawnToRaft.WhyNot() ?? "ready") + ")");

				// the box, No
				b.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.4f);
				string text = SpawnToRaft.Box == null ? "" : string.Join(" | ", SpawnToRaft.Box.GetComponentsInChildren<UnityEngine.UI.Text>(true).Where(t => t.gameObject.activeInHierarchy).Select(t => t.text).ToArray());
				Check(ref ok, SpawnToRaft.Asking && text.Contains(SpawnToRaft.Question), "the box asks: " + text);
				Check(ref ok, SpawnToRaft.YesButton != null && SpawnToRaft.NoButton != null && SpawnToRaft.YesButton != SpawnToRaft.NoButton, "it has a Yes and a No");
				Check(ref ok, !menu.buttonHolderPanel.activeSelf, "the menu's buttons are hidden behind it");
				Screenshot(new[] { "spawn_box" });
				yield return new WaitForSecondsRealtime(0.5f);
				if (SpawnToRaft.NoButton != null) SpawnToRaft.NoButton.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.5f);
				Check(ref ok, !SpawnToRaft.Asking && menu.buttonHolderPanel.activeSelf && CanvasHelper.ActiveMenu == MenuType.PauseMenu, "No: the box is gone, the menu is back");
				Check(ref ok, (p.transform.position - c).magnitude < 3f, "No: the player is still in the pit");

				// again, Yes
				b.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.4f);
				Check(ref ok, SpawnToRaft.Asking, "a second click opens the box again");
				float health = p.Stats.stat_health.Value;
				if (SpawnToRaft.YesButton != null) SpawnToRaft.YesButton.onClick.Invoke();
				yield return new WaitForSecondsRealtime(0.3f);
				Check(ref ok, !SpawnToRaft.Asking && CanvasHelper.ActiveMenu != MenuType.PauseMenu, "Yes: the box and the menu are closed");
				yield return new WaitForSecondsRealtime(3f);
				Raft raft = ComponentManager<Raft>.Value;
				RaycastHit hit;
				bool onRaft = Physics.Raycast(p.transform.position, Vector3.down, out hit, 3f, ~(1 << LayerMask.NameToLayer("LocalPlayer")), QueryTriggerInteraction.Ignore)
					&& raft != null && hit.collider.transform.IsChildOf(raft.transform);
				Check(ref ok, onRaft, "Yes: the player stands on the raft (" + (hit.collider != null ? hit.collider.name : "nothing") + " below, "
					+ (p.transform.position - CustomIslandSpawner.RaftPosition.Value).magnitude.ToString("F1") + " m from its middle, y " + p.transform.position.y.ToString("F1") + ")");
				Check(ref ok, p.PersonController.controllerType == ControllerType.Ground, "... walking, not swimming");
				Check(ref ok, p.Stats.stat_health.Value >= health - 0.5f, "... with no fall damage (health " + health.ToString("F0") + " -> " + p.Stats.stat_health.Value.ToString("F0") + ")");
			}
			finally { Object.Destroy(pit); }
			if (ok) Log("PASS: spawn to raft"); else Fail("spawn to raft");
		}

		static void CallPrivate(object o, string method) { HarmonyLib.Traverse.Create(o).Method(method).GetValue(); }
	}
}
