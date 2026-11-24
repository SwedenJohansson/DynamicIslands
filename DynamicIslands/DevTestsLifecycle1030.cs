using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests for the island life cycle review of 10-30: Raft's forced story islands and custom islands (H1).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIForcedStoryPoint", docs: "Dev, world (host): Raft's own story island (ChunkManager.AddChunkPointForcibly, what the Receiver calls) with custom islands in its way: it is moved on until clear of them (at most ForcedStoryPointPatch.MaxPushes times, so a sea full of them can't hang Raft), and an unloaded Raft island next to a custom island elsewhere is not deleted by Raft's clean-up. All in one frame with stand-in islands (no island spawns); the points and islands it adds are taken out after")]
		public static void ForcedStoryPointCommand() { StartTest(ForcedStoryPointRoutine()); }

		static IEnumerator ForcedStoryPointRoutine()
		{
			yield return null;
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("forced story point: host, in a world"); yield break; }
			ChunkManager cm = ComponentManager<ChunkManager>.Value;
			if (cm == null || cm.RaftTransform == null) { Fail("forced story point: no ChunkManager"); yield break; }
			bool ok = true;

			// One of Raft's story types without a biome zone (a test point must not leave one behind)
			SO_ChunkSpawnRuleAsset rule = null;
			ChunkPointType type = ChunkPointType.None;
			foreach (ChunkPointType t in StoryOrder.Chain)
			{
				SO_ChunkSpawnRuleAsset r = cm.GetRuleFromPointType(t);
				if (r != null && (int)r.BiomeZoneID == 0) { rule = r; type = t; break; }
			}
			// A rule for the stand-in Raft island: any of Raft's non-story, non-wreck points
			ChunkPoint sample = cm.GetAllChunkPointsList().FirstOrDefault(c => c != null && c.rule != null && !c.IsAddedByForce
				&& Array.IndexOf(StoryOrder.Chain, c.rule.ChunkPointType) < 0 && c.rule.name.IndexOf("FloatingRaft", StringComparison.OrdinalIgnoreCase) < 0);
			if (rule == null || sample == null) { Fail("forced story point: no story rule without a biome zone (" + type + ") or no Raft island to copy"); yield break; }
			MethodInfo fits = typeof(ChunkManager).GetMethod("DoesPointFit", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(ChunkPoint) }, null);

			float angleMin = rule.SpawnAngle.minValue, angleMax = rule.SpawnAngle.maxValue;
			float distMin = rule.SpawnDistanceFromRaft.minValue, distMax = rule.SpawnDistanceFromRaft.maxValue;
			var ids = new List<int>();
			var names = new List<string>();
			var added = new List<ChunkPoint>();
			try
			{
				// Raft's random spot fixed for the test: the first spot is known
				float angle = angleMin, dist = Mathf.Max(distMin, 300f);
				rule.SpawnAngle.minValue = rule.SpawnAngle.maxValue = angle;
				rule.SpawnDistanceFromRaft.minValue = rule.SpawnDistanceFromRaft.maxValue = dist;
				Vector3 raft = cm.RaftTransform.position;
				Vector3 dir = Helper.XZDirectionFromAngle(angle);
				Vector3 first = raft + dir * dist;
				Vector3 side = new Vector3(dir.z, 0f, -dir.x);

				// A stand-in Raft island far to the side, not loaded, that fits where it is
				ChunkPoint standIn = null;
				for (int k = 0; k < 8 && standIn == null; k++)
				{
					var c = new ChunkPoint { worldPosition = raft + side * (3000f + 400f * k), rule = sample.rule };
					if ((bool)fits.Invoke(cm, new object[] { c })) standIn = c;
				}
				if (standIn == null) { Fail("forced story point: no free spot for a stand-in Raft island"); yield break; }
				cm.chunkPoints[standIn.rule.ChunkPointType].Add(standIn);
				added.Add(standIn);

				// Custom islands (stand-ins: sizes cached, no files): two on the story island's spot, one on the stand-in Raft island
				Func<string, Vector3, float, IslandWorldState.Entry> standInIsland = (n, at, radius) =>
				{
					CustomIslandSpawner.CacheSize(n, radius, 0f);
					names.Add(n);
					IslandWorldState.Entry e = IslandWorldState.Add(n, at, null, false);
					ids.Add(e.Id);
					return e;
				};
				standInIsland("ci-lc-story-a", first, 150f);
				standInIsland("ci-lc-story-b", first + dir * 200f, 150f);
				standInIsland("ci-lc-beside", standIn.worldPosition, 150f);

				List<ChunkPoint> before = cm.GetAllChunkPointsList().Where(c => c != null).ToList();
				var fitBefore = new Dictionary<ChunkPoint, bool>();
				foreach (ChunkPoint c in before) fitBefore[c] = (bool)fits.Invoke(cm, new object[] { c });
				Check(ref ok, !fitBefore[standIn], "Raft's check (with the mod's) says the stand-in is too near a custom island now");

				// Raft forces its story island
				ChunkPoint cp = cm.AddChunkPointForcibly(type);
				if (cp != null) added.Add(cp);
				Check(ref ok, !ForcedStoryPointPatch.Forcing, "the forcing flag is off after Raft's call");
				Check(ref ok, cp != null && cp.IsAddedByForce && cm.GetAllChunkPointsList().Contains(cp), "Raft added its " + StoryOrder.Name(type) + " point");
				if (cp != null)
				{
					float along = new Vector2(cp.worldPosition.x - raft.x, cp.worldPosition.z - raft.z).magnitude;
					Check(ref ok, !ChunkPointFitPatch.NearCustomIsland(cp), "the story point is clear of every custom island (" + along.ToString("F0") + " m from the raft, first spot " + dist.ToString("F0") + " m)");
					Check(ref ok, ForcedStoryPointPatch.LastPushes >= 2 && along >= dist + 200f * ForcedStoryPointPatch.LastPushes - 1f,
						"Raft's own loop moved it on " + ForcedStoryPointPatch.LastPushes + " time(s) of 200 m for the custom islands");
				}
				List<ChunkPoint> after = cm.GetAllChunkPointsList();
				Check(ref ok, after.Contains(standIn), "the unloaded Raft island next to a custom island is kept (Raft's clean-up after a forced add)");
				List<ChunkPoint> gone = before.Where(c => !after.Contains(c)).ToList();
				List<ChunkPoint> wrong = gone.Where(c => fitBefore[c] && !(cp != null && Overlaps(c, cp))).ToList();
				Check(ref ok, wrong.Count == 0, gone.Count + " Raft point(s) removed, none for a custom island: only ones Raft's own rules drop" + (wrong.Count > 0 ? " (not: " + string.Join(", ", wrong.Select(c => c.rule.name + " at " + c.worldPosition.ToString("F0")).ToArray()) + ")" : ""));

				// A sea full of custom islands: Raft's loop has no cap, ours stops after MaxPushes
				if (cp != null) { cm.chunkPoints[type].Remove(cp); added.Remove(cp); cp.OnDestroyed(); }
				standInIsland("ci-lc-huge", first, 100000f);
				float t0 = Time.realtimeSinceStartup;
				ChunkPoint cp2 = cm.AddChunkPointForcibly(type);
				if (cp2 != null) added.Add(cp2);
				Check(ref ok, cp2 != null && ForcedStoryPointPatch.LastPushes == ForcedStoryPointPatch.MaxPushes,
					"no free spot at all: Raft's call returns after " + ForcedStoryPointPatch.LastPushes + " pushes (" + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("F0") + " ms)");
				Check(ref ok, !ForcedStoryPointPatch.Forcing, "the forcing flag is off again");
			}
			finally
			{
				rule.SpawnAngle.minValue = angleMin; rule.SpawnAngle.maxValue = angleMax;
				rule.SpawnDistanceFromRaft.minValue = distMin; rule.SpawnDistanceFromRaft.maxValue = distMax;
				foreach (ChunkPoint c in added)
				{
					List<ChunkPoint> list;
					if (cm.chunkPoints.TryGetValue(c.rule.ChunkPointType, out list)) list.Remove(c);
					c.OnDestroyed();
				}
				IslandWorldState.RemoveIds(ids, false);
				foreach (string n in names) CustomIslandSpawner.ForgetSize(n);
				cm.isDirty = true;
			}
			if (ok) Log("PASS: Raft's forced story islands keep clear of custom islands"); else Fail("Raft's forced story islands keep clear of custom islands");
		}

		/// <summary>Raft's own overlap test of DoesPointFit between two points.</summary>
		static bool Overlaps(ChunkPoint a, ChunkPoint b)
		{
			float d = new Vector2(a.worldPosition.x - b.worldPosition.x, a.worldPosition.z - b.worldPosition.z).magnitude;
			return d <= a.rule.collisionOverlapRadius + b.rule.collisionOverlapRadius || !a.rule.DoesPointPassSpawnRules(a, b);
		}
	}
}
