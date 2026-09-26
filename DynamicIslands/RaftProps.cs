using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>A Raft object as measured by CIMeasureProps: its size and where its bottom is, at the scale it spawns with.</summary>
	public class PropInfo
	{
		public string Name, Scene;
		/// <summary>Bounds of its meshes relative to its pivot, at its own scale (m).</summary>
		public Vector3 Size, Centre;
		/// <summary>How far its lowest point is below the pivot (negative: below) - what to lift it by to stand it on the ground.</summary>
		public float Bottom;
		public int Colliders;
		/// <summary>Cave pieces: the local axis of the passage (0 = x, 2 = z), the floor inside (above the pivot) and the room above it,
		/// and which ends are open (+axis, -axis).</summary>
		public int Axis = -1;
		public float Floor, Headroom, Width;
		public bool OpenPlus, OpenMinus;
		/// <summary>A point in the passage (from the pivot), and how much room there is from it towards +axis and -axis (99 = open).</summary>
		public Vector3 Inside;
		public float ToPlus = 99f, ToMinus = 99f;
		public bool IsCave { get { return Axis >= 0; } }
	}

	/// <summary>
	/// Sizes and footprints of Raft's objects the randomizer builds with (props from the quest islands, cave pieces):
	/// raft_props.txt, measured by the dev command CIMeasureProps and shipped with the mod, so islands can be made
	/// without loading Raft's island scenes first.
	/// </summary>
	public static class RaftProps
	{
		public const string FileName = "raft_props.txt";
		static Dictionary<string, PropInfo> props;

		public static void Reload() { props = null; }

		public static PropInfo Get(string name)
		{
			if (props == null) Load();
			PropInfo p;
			return name != null && props.TryGetValue(name, out p) ? p : null;
		}

		public static bool Has(string name) { return Get(name) != null; }

		/// <summary>
		/// A measured prop that stands on the ground: not one that hangs from its pivot (awnings, banners, dream catchers,
		/// hanging signs and lamps: most of it well below the pivot), which would float with nothing holding it.
		/// </summary>
		public static bool Standable(string name)
		{
			PropInfo p = Get(name);
			return p != null && !(p.Centre.y < -0.25f && p.Bottom < -0.5f && p.Bottom < -0.6f * p.Size.y);
		}

		/// <summary>What to lift a prop by so it stands on the ground (0 if it wasn't measured).</summary>
		public static float Lift(string name) { PropInfo p = Get(name); return p != null ? -p.Bottom : 0f; }

		public static IEnumerable<PropInfo> All { get { if (props == null) Load(); return props.Values; } }

		static void Load()
		{
			props = new Dictionary<string, PropInfo>(StringComparer.Ordinal);
			byte[] bytes = RaftIslands.ModFile(FileName);
			if (bytes == null) { Debug.LogWarning("[CUSTOM ISLANDS] " + FileName + " is missing: props stand on their pivots"); return; }
			try
			{
				foreach (string raw in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
				{
					string line = raw.TrimEnd('\r');
					if (line.Length == 0 || line.StartsWith("#")) continue;
					string[] f = line.Split('\t');
					if (f[0] == "prop" && f.Length >= 7)
						props[f[1]] = new PropInfo { Name = f[1], Scene = f[2], Size = V(f[3]), Centre = V(f[4]), Bottom = F(f[5]), Colliders = int.Parse(f[6], CultureInfo.InvariantCulture) };
					else if (f[0] == "cave" && f.Length >= 8)
					{
						PropInfo p = Get(f[1]);
						if (p == null) continue;
						p.Axis = int.Parse(f[2], CultureInfo.InvariantCulture);
						p.Floor = F(f[3]); p.Headroom = F(f[4]); p.Width = F(f[5]);
						p.OpenPlus = f[6] == "1"; p.OpenMinus = f[7] == "1";
						if (f.Length >= 11) { p.Inside = V(f[8]); p.ToPlus = F(f[9]); p.ToMinus = F(f[10]); }
					}
				}
				if (props.Count == 0) Debug.LogWarning("[CUSTOM ISLANDS] " + FileName + " has no props: props stand on their pivots");
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + FileName + ": " + e.Message); }
		}

		static float F(string s) { return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }

		static Vector3 V(string s)
		{
			string[] p = s.Split(',');
			return new Vector3(F(p[0]), F(p[1]), F(p[2]));
		}

		public static string Text(Vector3 v) { return v.x.ToString("0.###", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.###", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.###", CultureInfo.InvariantCulture); }
	}
}
