using System.Collections;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Guide pictures that need a world set up first (ROADMAP D6): Raft's Receiver with the custom islands' green dots.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIReceiverShot", docs: "Dev, in game (host): a Receiver with its screen in front of the camera with its radar on, custom islands round the raft (up to 3 brought), and its picture shot_receiver.png for the guide. CIReceiverShot [turn degrees] [metres ahead] [metres down]")]
		public static void ReceiverShot(string[] args)
		{
			float turn = args != null && args.Length > 0 ? float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 0f;
			float ahead = args != null && args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0.5f;
			float down = args != null && args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0.05f;
			StartTest(ReceiverShotRoutine(turn, ahead, down));
		}

		static IEnumerator ReceiverShotRoutine(float turn, float ahead, float down)
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("receiver shot: run in a world, as the host"); yield break; }
			// A few islands round the raft, so the radar has dots in more than one direction
			Vector3[] around = { new Vector3(250f, 0, 420f), new Vector3(-480f, 0, 120f), new Vector3(150f, 0, -520f) };
			string[] names = IslandSpawner.ListSavedIslands().Where(n => !n.StartsWith("ci")).Take(3).ToArray();
			for (int i = IslandWorldState.Islands.Count; i < 3 && i < names.Length; i++)
				yield return DynamicIslands.instance.SpawnIslandFile(names[i], raftPos.Value + around[i], true);
			Reciever prefab = null;
			foreach (Item_Base item in ItemManager.GetAllItems())
			{
				try
				{
					if (item == null || item.settings_buildable == null || !item.settings_buildable.Placeable) continue;
					Block[] blocks = item.settings_buildable.GetBlockPrefabs();
					prefab = blocks != null ? blocks.Where(b => b != null).Select(b => b.GetComponentInChildren<Reciever>(true)).FirstOrDefault(rc => rc != null) : null;
					if (prefab != null) break;
				}
				catch { }
			}
			if (prefab == null) { Fail("receiver shot: could not find Raft's receiver block"); yield break; }
			Transform cam = Camera.main.transform;
			Vector3 flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
			GameObject go = UnityEngine.Object.Instantiate(prefab.transform.root.gameObject, cam.position + flat * 3f, Quaternion.identity);
			Reciever r = go.GetComponentInChildren<Reciever>(true);
			Transform screen = r.canvasSection != null ? r.canvasSection.transform : r.dotParent;
			// The screen (a small world-space canvas) faces the camera, ahead metres away and down metres below the eye
			go.transform.RotateAround(screen.position, Vector3.up, Vector3.SignedAngle(Vector3.ProjectOnPlane(screen.forward, Vector3.up), flat, Vector3.up) + turn);
			go.transform.position += cam.position + flat * ahead + Vector3.down * down - screen.position;
			cam.rotation = Quaternion.LookRotation(screen.position - cam.position);
			yield return null;
			// The screen is off without a charged battery: Raft's own update (which would switch it off again) stops, and
			// everything above the dots goes on
			r.enabled = false;
			if (r.radarSection != null) r.radarSection.SetActive(true);
			for (Transform t = r.dotParent; t != null && t != go.transform; t = t.parent) t.gameObject.SetActive(true);
			foreach (Canvas c in go.GetComponentsInChildren<Canvas>(true)) c.enabled = true;
			for (int i = 0; i < 30; i++) { IslandRadar.Draw(r); cam.rotation = Quaternion.LookRotation(screen.position - cam.position); yield return null; }
			int dots = IslandRadar.DotsOf(r).Count(d => d != null && d.gameObject.activeSelf);
			Screenshot(new[] { "receiver" });
			yield return new WaitForSeconds(1f);
			UnityEngine.Object.Destroy(go);
			Log((dots > 0 ? "PASS" : "FAIL") + ": receiver shot with " + dots + " green dot(s)");
		}
	}
}
