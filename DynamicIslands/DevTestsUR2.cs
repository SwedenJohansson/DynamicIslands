using System.Collections;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIHoldMissingIsland", docs: "Dev, host in a world: (a) held near an island whose file was deleted (left out) - let go within 5 s, not after 2 min; (b) held near an island still loading, then moved to the raft (gotoraft) - let go, stays on the raft (UR2)")]
		public static void HoldMissingIslandCommand()
		{
			StartTest(HoldMissingIsland());
		}

		static IEnumerator HoldMissingIsland()
		{
			Network_Player p = RAPI.GetLocalPlayer();
			Raft r = Object.FindObjectOfType<Raft>();
			if (p == null || r == null) { Fail("hold missing island: no player or raft"); yield break; }
			Vector3 raftAt = r.transform.position;
			Vector3 at = raftAt + new Vector3(150f, 6f, 0f);
			// (a) an island deleted from Mods: on the host its entry waits for a file nobody sends, and is left out
			IslandWorldState.Entry gone = new IslandWorldState.Entry { Id = -9201, Name = "ci_ur2_deleted", HostName = "ci_ur2_deleted", Position = new Vector3(at.x, 0f, at.z + 40f), WaitingForFile = true, Excluded = true };
			IslandWorldState.Islands.Add(gone);
			float took;
			try
			{
				PlayerHold.TestHold(at);
				float t0 = Time.unscaledTime;
				while (PlayerHold.Busy && Time.unscaledTime - t0 < 10f) yield return null;
				took = Time.unscaledTime - t0;
			}
			finally { IslandWorldState.Islands.Remove(gone); }
			bool aOk = !PlayerHold.Busy && took < 5f;
			if (aOk) Log("hold missing island (a): let go after " + took.ToString("F1") + " s near a deleted island");
			else { Fail("hold missing island (a): still held after " + took.ToString("F1") + " s near a deleted island"); PlayerHold.LetGo("Dev: test over"); }
			PlayerMove.To(p, raftAt + Vector3.up * 2f);
			yield return new WaitForSecondsRealtime(1f);
			// (b) an island still loading under the hold: the player goes to the raft (gotoraft, Unstuck) - not put back
			IslandWorldState.Entry coming = new IslandWorldState.Entry { Id = -9202, Name = "ci_ur2_coming", HostName = "ci_ur2_coming", Position = new Vector3(at.x, 0f, at.z), Loading = true };
			IslandWorldState.Islands.Add(coming);
			bool heldFirst, letGo; float off;
			try
			{
				PlayerHold.TestHold(at);
				yield return new WaitForSecondsRealtime(1f);
				heldFirst = PlayerHold.Busy && Vector3.Distance(p.transform.position, at) < 3f;
				p.transform.position = raftAt + Vector3.up * 2f; // (as the console's gotoraft does)
				yield return new WaitForSecondsRealtime(1f);
				letGo = !PlayerHold.Busy;
				off = new Vector2(p.transform.position.x - raftAt.x, p.transform.position.z - raftAt.z).magnitude;
			}
			finally { IslandWorldState.Islands.Remove(coming); PlayerHold.LetGo("Dev: test over"); }
			bool bOk = heldFirst && letGo && off < 20f;
			if (bOk) Log("hold missing island (b): held, then moved to the raft - let go, " + off.ToString("F0") + " m from the raft");
			else Fail("hold missing island (b): held first " + heldFirst + ", let go " + letGo + ", " + off.ToString("F0") + " m from the raft");
			if (aOk && bOk) Log("PASS: hold missing island");
		}
	}
}
