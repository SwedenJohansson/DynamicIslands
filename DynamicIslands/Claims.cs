using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Things only one player can have: a chest's loot, a zone that fires once. Every machine knew "used" only from its
	/// own copy of the island's state, so two players opening one chest within a message's time both got its loot (and
	/// with eight players it is only more likely). A client now asks the host first (IslandNetMessage.Claim); the host
	/// grants the first to ask and holds the thing for them a few seconds, until their "used" arrives - everyone else is
	/// told no. The host's own player goes through the same hold. A host without claims (an older Custom Islands) never
	/// answers: the client goes ahead after a moment, as before.
	/// </summary>
	public static class Claims
	{
		/// <summary>How long the host keeps a granted thing for its player (their "used" comes well before).</summary>
		internal const float HoldSeconds = 6f;
		/// <summary>A client with no answer by then goes ahead (an older host).</summary>
		const float NoAnswerSeconds = 3f;

		// Host: island|key -> who has it, until when
		static readonly Dictionary<long, KeyValuePair<ulong, float>> held = new Dictionary<long, KeyValuePair<ulong, float>>();
		// Client: granted and not used yet; waiting for the host's answer
		static readonly HashSet<long> granted = new HashSet<long>();
		static readonly Dictionary<long, Action<bool>> waiting = new Dictionary<long, Action<bool>>();

		/// <summary>Tests: the last answer this machine got or gave ("granted" / "refused").</summary>
		public static string LastAnswer { get; private set; }

		static long K(int island, int key) { return ((long)island << 32) | (uint)key; }

		static ulong LocalId { get { Network_Player p = RAPI.GetLocalPlayer(); return p != null ? p.steamID.Id : 0UL; } }

		/// <summary>Leaving the world: nothing held or asked any more.</summary>
		public static void Reset() { held.Clear(); granted.Clear(); waiting.Clear(); }

		/// <summary>
		/// An island is unloaded or removed on this machine: this player's grants and questions for its things are
		/// forgotten (a late answer is then ignored, and the next try asks again). The host's holds stay: they end on
		/// their own within seconds, and another player may still have the island loaded.
		/// </summary>
		public static void ForgetIsland(int islandId)
		{
			int forgotten = granted.RemoveWhere(x => (int)(x >> 32) == islandId);
			foreach (long k in waiting.Keys.Where(x => (int)(x >> 32) == islandId).ToList()) { waiting.Remove(k); forgotten++; }
			if (forgotten > 0) Debug.Log("[CUSTOM ISLANDS] [net] Island " + islandId + " unloaded: " + forgotten + " claim(s) of it forgotten");
		}

		/// <summary>
		/// Whether this player may use the thing now. The host: unless another player holds it. A client: when the host
		/// granted it (asked earlier); otherwise it asks the host now and then(granted) runs when the answer comes.
		/// </summary>
		public static bool May(IslandWorldState.Entry e, int key, Action<bool> then)
		{
			if (e == null) return true;
			if (Raft_Network.IsHost) return HostGrant(e, key, LocalId);
			if (!IslandNetwork.InGame) return true;
			long k = K(e.Id, key);
			if (granted.Remove(k)) return true;
			if (waiting.ContainsKey(k)) { Debug.Log("[CUSTOM ISLANDS] [net] Claim of " + key.ToString("X") + " on island " + e.Id + ": asked already, waiting for the host"); return false; } // (the answer is on its way)
			waiting[k] = then;
			Debug.Log("[CUSTOM ISLANDS] [net] Asking the host for " + key.ToString("X") + " on island " + e.Id);
			IslandNetwork.SendClaim(e.Id, key);
			DynamicIslands.instance.StartCoroutine(NoAnswer(k));
			return false;
		}

		static IEnumerator NoAnswer(long k)
		{
			yield return new WaitForSeconds(NoAnswerSeconds);
			Action<bool> then;
			if (!waiting.TryGetValue(k, out then)) yield break;
			// A host that answers claims is only late (busy sending island files): its answer is waited for - going ahead
			// let two players loot one chest and set off a once-zone twice
			if (IslandNetwork.HostAnswersClaims)
			{
				Debug.Log("[CUSTOM ISLANDS] [net] The host hasn't answered a claim yet (busy?): waiting for it");
				IslandInfo.ShowMessage("Waiting for the host's answer (it is busy) - this happens as soon as it answers");
				// (asked again now and then: an answer - or the question - lost on the way left the player standing in a zone
				// that never went off, as they never walked in again - ROADMAP M2)
				for (float waited = 0f; waited < LateAnswerSeconds && waiting.ContainsKey(k); waited += AskAgainSeconds)
				{
					yield return new WaitForSeconds(AskAgainSeconds);
					if (!waiting.ContainsKey(k)) yield break;
					Debug.Log("[CUSTOM ISLANDS] [net] Asking the host again for " + ((int)(uint)k).ToString("X") + " on island " + (int)(k >> 32));
					IslandNetwork.SendClaim((int)(k >> 32), (int)(uint)k);
				}
				// (no answer at all: the next try asks again)
				if (waiting.ContainsKey(k)) { waiting.Remove(k); Debug.LogWarning("[CUSTOM ISLANDS] [net] No answer to a claim from the host: try again"); IslandInfo.ShowMessage("The host didn't answer - try again"); }
				yield break;
			}
			waiting.Remove(k);
			Debug.LogWarning("[CUSTOM ISLANDS] [net] The host didn't answer a claim (an older Custom Islands?): going ahead");
			granted.Add(k);
			try { then(true); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Claim: " + ex.Message); }
		}

		/// <summary>How long a late answer of a host that answers claims is waited for.</summary>
		const float LateAnswerSeconds = 30f;
		/// <summary>How often a client asks again meanwhile.</summary>
		const float AskAgainSeconds = 5f;

		/// <summary>Tests (CIClaimDelay): the host answers claims this many seconds late, as a host busy sending island files.</summary>
		public static float TestAnswerDelay;

		static IEnumerator AnswerLater(float seconds, Action send)
		{
			yield return new WaitForSecondsRealtime(seconds);
			try { send(); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Claim answer: " + ex.Message); }
		}

		/// <summary>Host: may this player have it? Grants the first to ask (not used yet, nobody else holding it) and holds it for them.</summary>
		public static bool HostGrant(IslandWorldState.Entry e, int key, ulong who)
		{
			if (e == null) return false;
			long k = K(e.Id, key);
			KeyValuePair<ulong, float> h;
			bool heldByOther = held.TryGetValue(k, out h) && h.Key != who && Time.unscaledTime < h.Value;
			bool ok = !ContentState.IsUsed(e, key) && !heldByOther;
			if (ok) held[k] = new KeyValuePair<ulong, float>(who, Time.unscaledTime + HoldSeconds);
			LastAnswer = ok ? "granted" : "refused";
			return ok;
		}

		/// <summary>From the network: a client asks (host), or the host answers (client: Count 1 = granted).</summary>
		public static void OnMessage(IslandNetMessage msg, ulong from, Action<IslandNetMessage> reply)
		{
			if (msg.Ids == null || msg.Ids.Length == 0) return;
			if (Raft_Network.IsHost)
			{
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == msg.Ids[0]);
				bool ok = e != null && HostGrant(e, msg.Index, from);
				Debug.Log("[CUSTOM ISLANDS] [net] Claim of " + msg.Index.ToString("X") + " on island " + msg.Ids[0] + " by " + from + ": " + (ok ? "granted" : "refused"));
				var answer = new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = msg.Ids, Index = msg.Index, Count = ok ? 1 : 0 };
				if (TestAnswerDelay > 0f) { DynamicIslands.instance.StartCoroutine(AnswerLater(TestAnswerDelay, () => reply(answer))); return; }
				reply(answer);
				return;
			}
			IslandNetwork.HostAnswersClaims = true;
			long k = K(msg.Ids[0], msg.Index);
			Action<bool> then;
			if (!waiting.TryGetValue(k, out then)) return;
			waiting.Remove(k);
			bool yes = msg.Count == 1;
			Debug.Log("[CUSTOM ISLANDS] [net] The host " + (yes ? "granted" : "refused") + " " + msg.Index.ToString("X") + " on island " + msg.Ids[0]);
			LastAnswer = yes ? "granted" : "refused";
			if (yes) granted.Add(k);
			try { then(yes); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Claim: " + ex.Message); }
		}
	}
}
