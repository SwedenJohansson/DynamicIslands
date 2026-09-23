using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Custom islands show up on Raft's Receiver (roadmap 1.4 "how to find islands"; Discord ideas: coordinates /
	/// radio codes for custom islands). Raft's receiver draws a dot per signal (ChunkManager points) in HandleUI
	/// every frame; after it, we draw our own dots, one per island in the world's island list, the same way:
	/// direction relative to the receiver, distance clamped to the radar edge, "123m" label. They are tinted green
	/// so they can be told apart from story signals. Purely local UI: every player's receiver shows the islands that
	/// player's game knows about (clients get the list from the host). Off with showOnReceiver = 0 in spawnpool.txt.
	/// </summary>
	[HarmonyPatch(typeof(Reciever), "HandleUI")]
	static class IslandRadar
	{
		static readonly Color DotColor = new Color(0.35f, 1f, 0.45f);
		static readonly Dictionary<Reciever, List<Reciever_Dot>> dots = new Dictionary<Reciever, List<Reciever_Dot>>();

		static void Postfix(Reciever __instance)
		{
			try { Draw(__instance); }
			catch (System.Exception e)
			{
				// Never break Raft's own receiver; report once per receiver
				if (!failed.Contains(__instance)) { failed.Add(__instance); Debug.LogWarning("[CUSTOM ISLANDS] Receiver dots: " + e); }
			}
		}

		static readonly HashSet<Reciever> failed = new HashSet<Reciever>();

		/// <summary>Dots currently drawn on a receiver (dev tests).</summary>
		internal static List<Reciever_Dot> DotsOf(Reciever r)
		{
			List<Reciever_Dot> list;
			return dots.TryGetValue(r, out list) ? list : new List<Reciever_Dot>();
		}

		internal static void Draw(Reciever r)
		{
			if (r == null || r.dotPrefab == null || r.dotParent == null) return;
			List<Reciever_Dot> list;
			if (!dots.TryGetValue(r, out list))
			{
				foreach (Reciever dead in dots.Keys.Where(k => k == null).ToList()) dots.Remove(dead);
				dots[r] = list = new List<Reciever_Dot>();
			}

			bool show = CustomIslandSpawner.ShowOnReceiver && r.radarSection != null && r.radarSection.activeSelf;
			IList<IslandWorldState.Entry> islands = IslandWorldState.Islands;
			int wanted = show ? islands.Count : 0;
			while (list.Count < wanted)
			{
				Reciever_Dot d = Object.Instantiate(r.dotPrefab, r.dotParent);
				d.transform.localScale = r.dotPrefab.transform.localScale;
				foreach (Graphic g in d.GetComponentsInChildren<Graphic>(true)) if (!(g is Text)) g.color = DotColor;
				list.Add(d);
			}

			Vector3 rp = r.transform.position;
			for (int i = 0; i < list.Count; i++)
			{
				Reciever_Dot d = list[i];
				if (d == null) continue;
				bool on = i < wanted;
				if (d.gameObject.activeSelf != on) d.gameObject.SetActive(on);
				if (!on) continue;

				// Same maths as Reciever.HandleUI: angle from the receiver's forward, distance clamped to the radar edge
				Vector3 p = islands[i].Position; // follows world shifts, also while the island is unloaded
				Vector2 delta = new Vector2(p.x - rp.x, p.z - rp.z);
				float a = Vector2.SignedAngle(Vector2.up, delta);
				if (a < 0f) a += 360f;
				a = 360f - a;
				float rad = (360f - r.transform.eulerAngles.y + a) * Mathf.Deg2Rad;
				Vector3 dir = new Vector3(Mathf.Sin(rad), Mathf.Cos(rad), 0f) * -1f;
				float dist = delta.magnitude;
				d.SetTargetedByReciever(true);
				d.SetLocalPosition(dir * (r.radarUIWidth * 0.5f * Mathf.Clamp01(dist / r.radarLength)));
				d.SetLengthToPoint(dist);
				d.SetText(dist.ToString("F0") + "m");
				r.isCurrentlyShowingRadarDot = true;
			}
		}
	}
}
