using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The generator's things to gather (ROADMAP LM9, the user 2026-10-05: options for gatherables on land and under
	/// water, little or much): Gather spreads what suits the island's style on the land - trees to cut, fruit, berries,
	/// flowers - and Shallows Raft's sea finds just off the shore (sand, clay, stone, iron and copper ore, scrap, giant
	/// clams, seaweed), as the library's islands have them. Both 0 by default: islands made before are unchanged.
	/// </summary>
	public static class GenGather
	{
		/// <summary>The style's things to gather (by TerrainPainter style), each with whether it is a tree (more room).</summary>
		static readonly Dictionary<int, KeyValuePair<string, bool>[]> Kinds = new Dictionary<int, KeyValuePair<string, bool>[]>
		{
			{ TerrainPainter.Tropical, new[] { T("Pickup_Landmark_Tree_Palm 1"), T("Pickup_Landmark_Tree_Palm 3"), T("Pickup_Landmark_Tree_Mango"), S("Pickup_Landmark_PineappleLandmark"),
				S("Pickup_Landmark_WatermelonLandmark"), S("Banana_Bush_2"), S("Pickup_Landmark_Flower_Red"), S("Pickup_Landmark_Flower_Yellow") } },
			{ 1, new[] { T("Pickup_Landmark_Tree_Pine"), S("Pickup_Landmark_BerryBush"), S("Pickup_Landmark_Flower_White"), S("Pickup_Landmark_Flower_Blue") } },
			{ 2, new[] { T("Pickup_Landmark_Tree_Palm 2"), T("Pickup_Landmark_Tree_Palm 4"), S("Pickup_Landmark_PineappleLandmark"), S("Pickup_Landmark_WatermelonLandmark"),
				S("Pickup_Landmark_Flower_Yellow"), S("Pickup_Landmark_Flower_Red") } },
			{ 3, new[] { T("Pickup_Landmark_Tree_Birch"), T("Pickup_Landmark_Tree_Pine"), S("Pickup_Landmark_BerryBush"), S("Pickup_Landmark_Flower_Blue"), S("Pickup_Landmark_Flower_White"),
				S("Pickup_Landmark_Flower_Red") } },
			{ 4, new[] { T("Pickup_Landmark_Tree_Palm 3"), T("Pickup_Landmark_Tree_Mango"), S("Pickup_Landmark_PineappleLandmark"), S("Pickup_Landmark_Flower_Black"), S("Pickup_Landmark_Flower_Red") } },
		};
		static KeyValuePair<string, bool> T(string n) { return new KeyValuePair<string, bool>(n, true); }
		static KeyValuePair<string, bool> S(string n) { return new KeyValuePair<string, bool>(n, false); }

		/// <summary>Raft's sea finds in the shallows, each with its share (lib_sea's shallows).</summary>
		static readonly KeyValuePair<string, int>[] SeaFinds =
		{
			W("Pickup_Landmark_Sand", 4), W("Pickup_Landmark_Clay 1", 2), W("Pickup_Landmark_Clay 2", 2), W("Pickup_Landmark_Rock 1", 2), W("Pickup_Landmark_Rock 2", 2),
			W("Pickup_Landmark_Iron 1", 2), W("Pickup_Landmark_Iron 3", 1), W("Pickup_Landmark_Copper 1", 1), W("Pickup_Landmark_Scrap 1_OceanBottom", 2),
			W("Pickup_Landmark_Scrap 3_OceanBottom", 1), W("Pickup_Landmark_GiantClam", 1), W("SeaVine3_klump", 2),
		};
		static KeyValuePair<string, int> W(string n, int w) { return new KeyValuePair<string, int>(n, w); }

		/// <summary>At 1 (much): this many things to gather per 1000 m2 of land, sea finds per 1000 m2 of shallows.</summary>
		public const float LandPer1000 = 10f, SeaPer1000 = 30f;
		public const int MaxEach = 400;

		/// <summary>Adds the things to gather to a generated island's objects; returns how many (land, sea).</summary>
		public static KeyValuePair<int, int> Apply(IslandFile f, IslandGenSettings s, int seed)
		{
			if (s.Gather <= 0f && s.Shallows <= 0f) return new KeyValuePair<int, int>(0, 0);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1), sea = f.WaterLevel;
			Func<float, float, float> ground = (x, z) => IslandGenerator.SampleHeights(f.Heights, res, step, x, z) * f.TerrainSize.y;
			// (taken spots: a coarse grid of what the generator placed, so nothing lands on a tree, a rock or a building)
			var taken = new HashSet<long>();
			Func<float, float, long> cell = (x, z) => ((long)Mathf.FloorToInt(x) << 32) ^ (uint)Mathf.FloorToInt(z);
			foreach (IslandObject o in f.Objects) taken.Add(cell(o.Position.x, o.Position.z));
			Func<float, float, float, bool> free = (x, z, r) =>
			{
				int ir = Mathf.CeilToInt(r);
				for (int dx = -ir; dx <= ir; dx++) for (int dz = -ir; dz <= ir; dz++) if (dx * dx + dz * dz <= r * r && taken.Contains(cell(x + dx, z + dz))) return false;
				return true;
			};
			// The land and the shallows, counted on a 2 m grid
			float landArea = 0f, shallowArea = 0f;
			for (float x = 0; x < f.TerrainSize.x; x += 2f)
				for (float z = 0; z < f.TerrainSize.z; z += 2f)
				{
					float h = ground(x, z);
					if (h > sea + 0.3f) landArea += 4f; else if (h < sea - 0.6f && h > sea - 6f) shallowArea += 4f;
				}
			var rnd = new System.Random(seed ^ 0x6a7e);
			int land = 0, wet = 0;
			KeyValuePair<string, bool>[] kinds;
			if (s.Gather > 0f && Kinds.TryGetValue(s.Style, out kinds))
			{
				int want = Mathf.Min(MaxEach, Mathf.RoundToInt(landArea / 1000f * LandPer1000 * s.Gather));
				for (int k = 0; k < want * 30 && land < want; k++)
				{
					float x = (float)rnd.NextDouble() * f.TerrainSize.x, z = (float)rnd.NextDouble() * f.TerrainSize.z, h = ground(x, z);
					if (h < sea + 0.4f) continue;
					// (not on steep ground: a fruit bush or a palm on a cliff face)
					if (Mathf.Abs(ground(x + 1f, z) - h) > 0.8f || Mathf.Abs(ground(x, z + 1f) - h) > 0.8f) continue;
					var kind = kinds[rnd.Next(kinds.Length)];
					if (!free(x, z, kind.Value ? 4f : 2f)) continue;
					f.Objects.Add(new IslandObject { Name = kind.Key, Position = new Vector3(x, h, z), EulerRotation = new Vector3(0f, (float)(rnd.NextDouble() * 360.0), 0f) });
					taken.Add(cell(x, z));
					land++;
				}
			}
			if (s.Shallows > 0f)
			{
				int total = SeaFinds.Sum(w => w.Value);
				int want = Mathf.Min(MaxEach, Mathf.RoundToInt(shallowArea / 1000f * SeaPer1000 * s.Shallows));
				for (int k = 0; k < want * 30 && wet < want; k++)
				{
					float x = (float)rnd.NextDouble() * f.TerrainSize.x, z = (float)rnd.NextDouble() * f.TerrainSize.z, h = ground(x, z);
					if (h > sea - 0.6f || h < sea - 6f) continue;
					if (!free(x, z, 1.5f)) continue;
					int pick = rnd.Next(total);
					string name = SeaFinds[0].Key;
					foreach (var w in SeaFinds) { if (pick < w.Value) { name = w.Key; break; } pick -= w.Value; }
					f.Objects.Add(new IslandObject { Name = name, Position = new Vector3(x, h, z), EulerRotation = new Vector3(0f, (float)(rnd.NextDouble() * 360.0), 0f) });
					taken.Add(cell(x, z));
					wet++;
				}
			}
			return new KeyValuePair<int, int>(land, wet);
		}
	}
}
