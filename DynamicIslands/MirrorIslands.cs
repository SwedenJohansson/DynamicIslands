using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Mirrored islands (PF8, world option "mirrorislands", off by default): about one random custom island in three comes
	/// mirrored left to right, so the same file looks new in another world. Done on the freshly loaded IslandFile, before it
	/// is spawned (DynamicIslands.SpawnIslandFile), so the terrain, paint, colliders and the objects' grounding all come from
	/// the mirrored data exactly as from an unmirrored one:
	///   - heights [y, x] -> [y, res-1-x], paint and paint mask x -> R-1-x, object x -> TerrainSize.x - x and its rotation
	///     reflected (quaternion (x, -y, -z, w)). The land centre is mirrored too, so the island lands on the same spot, and
	///     its radius is the same. The objects' order and uids (R1b) don't change: the world's saved state still fits.
	///   - Only plain islands: a world entry with no rule and no label (random spawns, not plans or story), no quest, island
	///     settings only of info./rules./atmo., object settings only of looks/loot/creatures/notes/sounds/zones (no
	///     behaviours, events, zip lines or story items: those can hold positions or directions), and no tail but the uids.
	///   - The choice is fixed per world and island: a stable hash of the world's option seed and the island's name, the
	///     same on the host and every client (both have the seed and the entry's name).
	/// Looks only: the meshes themselves are not mirrored (a ship's wheel turns the same way), only where things stand.
	/// </summary>
	public static class MirrorIslands
	{
		/// <summary>About one island in this many is mirrored.</summary>
		public const int OneIn = 3;

		static readonly string[] IslandKeys = { "info.", "rules.regrow", "rules.levels", "rules.notbefore", "atmo." };
		static readonly string[] ObjectKeys = { "tint.", "creature.", "loot.", "note.", "sound.", "atmo.", "treasure.", "zone.", "col.mode", "obj.name", "set.piece" };
		static readonly string[] ObjectKeysNot = { "zone.push" };

		/// <summary>True when the island's content allows a mirror (see the class notes); says why not in reason.</summary>
		public static bool CanMirror(IslandFile island, out string reason)
		{
			reason = "";
			if (island == null || island.Heights == null || island.HeightmapResolution < 2 || !IslandSpawner.HasLand(island)) { reason = "no land"; return false; }
			if (island.Heights.GetLength(0) != island.HeightmapResolution || island.Heights.GetLength(1) != island.HeightmapResolution) { reason = "odd heightmap"; return false; }
			if (IslandQuest.HasQuest(island.Props)) { reason = "has a quest"; return false; }
			string bad = island.Props.Keys.FirstOrDefault(k => !IslandKeys.Any(p => k.StartsWith(p, StringComparison.Ordinal)));
			if (bad != null) { reason = "island setting " + bad; return false; }
			if (island.Tail.Keys.Any(k => k != StableIds.Tag)) { reason = "tail " + island.Tail.Keys.First(k => k != StableIds.Tag); return false; }
			foreach (IslandObject o in island.Objects)
			{
				if (o.Props == null) continue;
				bad = o.Props.Keys.FirstOrDefault(k => ObjectKeysNot.Any(p => k.StartsWith(p, StringComparison.Ordinal)) || !ObjectKeys.Any(p => k.StartsWith(p, StringComparison.Ordinal)));
				if (bad != null) { reason = o.Name + " setting " + bad; return false; }
			}
			if (island.HasPaint && island.Alphamaps.Length != island.AlphamapLayers * island.AlphamapResolution * island.AlphamapResolution) { reason = "odd paint"; return false; }
			return true;
		}

		/// <summary>The fixed choice for this world (seed) and island name: about one in OneIn.</summary>
		public static bool Picked(int seed, string name)
		{
			// FNV-1a: the same on every PC and Mono version (string.GetHashCode is not)
			uint h = 2166136261;
			foreach (char c in seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (name ?? "").ToLowerInvariant()) { h ^= c; h *= 16777619; }
			return h % OneIn == 0;
		}

		/// <summary>True when a world's island is spawned mirrored: the option on, a plain random entry, picked and allowed.</summary>
		public static bool ShouldMirror(IslandWorldState.Entry entry, IslandFile island)
		{
			string reason;
			return entry != null && WorldOptions.On(WorldOptions.MirrorIslands) && string.IsNullOrEmpty(entry.Rule) && string.IsNullOrEmpty(entry.Label)
				&& Picked(WorldOptions.Seed, entry.HostName ?? island.Name) && CanMirror(island, out reason);
		}

		/// <summary>Mirrors the island left to right (x) in place; twice gives the original back.</summary>
		public static void Mirror(IslandFile island)
		{
			int res = island.HeightmapResolution;
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res / 2; x++)
				{
					float t = island.Heights[y, x]; island.Heights[y, x] = island.Heights[y, res - 1 - x]; island.Heights[y, res - 1 - x] = t;
				}
			if (island.HasPaint)
			{
				int r = island.AlphamapResolution;
				for (int row = 0; row < island.AlphamapLayers * r; row++) FlipRow(island.Alphamaps, row * r, r);
				if (island.PaintMask != null && island.PaintMask.Length == r * r)
					for (int z = 0; z < r; z++) FlipRow(island.PaintMask, z * r, r);
			}
			float w = island.TerrainSize.x;
			foreach (IslandObject o in island.Objects)
			{
				o.Position = new Vector3(w - o.Position.x, o.Position.y, o.Position.z);
				o.EulerRotation = MirrorRotation(o.EulerRotation);
			}
		}

		/// <summary>A rotation reflected in the x axis (as seen in a mirror standing along z).</summary>
		public static Vector3 MirrorRotation(Vector3 euler)
		{
			Quaternion q = Quaternion.Euler(euler);
			return new Quaternion(q.x, -q.y, -q.z, q.w).eulerAngles;
		}

		static void FlipRow(byte[] a, int start, int n)
		{
			for (int i = 0; i < n / 2; i++) { byte t = a[start + i]; a[start + i] = a[start + n - 1 - i]; a[start + n - 1 - i] = t; }
		}
	}
}
