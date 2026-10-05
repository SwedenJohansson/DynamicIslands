using System;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>The generator's things to gather (ROADMAP LM9): off by default (the island is unchanged), a little and
		/// much on the land by style, Raft's sea finds in the shallows; all where they belong.</summary>
		[ConsoleCommand(name: "CIGenGather", docs: "Dev, anywhere: the generator's Things to gather and Finds in the shallows - off changes nothing, more gives more, land things above the sea, sea finds 0.6-6 m down")]
		public static void GenGatherCommand(string[] args)
		{
			bool ok = true;
			foreach (int style in new[] { TerrainPainter.Tropical, TerrainPainter.Snowy, TerrainPainter.Desert, TerrainPainter.Forest, TerrainPainter.Volcanic })
			{
				Func<float, float, IslandFile> make = (g, w) => IslandGenerator.CreateFile(new IslandGenSettings { Seed = 7300 + style, Style = style, Shape = IslandShapes.Round, Radius = 70f, Height = 16f, ObjectDensity = 0.5f, Gather = g, Shallows = w }, "cigengather");
				IslandFile off = make(0f, 0f), little = make(0.3f, 0.3f), much = make(1f, 1f);
				Func<IslandFile, IslandFile, List<IslandObject>> added = (f, b) => f.Objects.Skip(b.Objects.Count).ToList();
				Check(ref ok, off.Objects.Count == IslandGenerator.CreateFile(new IslandGenSettings { Seed = 7300 + style, Style = style, Shape = IslandShapes.Round, Radius = 70f, Height = 16f, ObjectDensity = 0.5f }, "cigengather").Objects.Count,
					"style " + style + ": off changes nothing (" + off.Objects.Count + " objects)");
				List<IslandObject> a1 = added(little, off), a2 = added(much, off);
				Func<IslandObject, bool> wet = o => ScatterTool.OnlyUnderWater(o.Name) || o.Name.Contains("Sand") || o.Name.Contains("Clay") || o.Name.Contains("Rock ") || o.Name.Contains("Iron") || o.Name.Contains("Copper");
				int l1 = a1.Count(o => !wet(o)), l2 = a2.Count(o => !wet(o)), w1 = a1.Count(wet), w2 = a2.Count(wet);
				Check(ref ok, l1 > 0 && l2 > l1 * 1.5f, "style " + style + ": things to gather on land, a little " + l1 + ", much " + l2 + " (" + string.Join(", ", a2.Where(o => !wet(o)).Select(o => o.Name.Replace("Pickup_Landmark_", "")).Distinct().ToArray()) + ")");
				Check(ref ok, w1 > 0 && w2 > w1 * 1.5f && w2 >= 10, "style " + style + ": finds in the shallows, a little " + w1 + ", much " + w2);
				if (style != TerrainPainter.Snowy && style != TerrainPainter.Desert)
					Check(ref ok, a2.Any(o => o.Name == ContentCatalog.WildHive && ObjectProps.Get(o.Props, ObjectProps.LootItems).Contains("HoneyComb")) && a2.Any(o => o.Name == "Pickup_Landmark_DirtPickup"),
						"style " + style + ": dirt spots and a wild beehive with honeycomb among them");
				float sea = much.WaterLevel;
				Check(ref ok, a2.Where(o => !wet(o)).All(o => o.Position.y > sea + 0.3f), "style " + style + ": land things above the sea");
				Check(ref ok, a2.Where(wet).All(o => o.Position.y < sea - 0.5f && o.Position.y > sea - 6.1f), "style " + style + ": sea finds 0.6-6 m down");
			}
			if (ok) Log("PASS: generator things to gather"); else Fail("generator things to gather");
		}
	}
}
