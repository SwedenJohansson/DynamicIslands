using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's features on islands and the tools players need for them, in one place (the user, 2026-10-16): vines want the
	/// machete, buried treasure the metal detector and the shovel, dirt spots the shovel, Raft's zipline lines the zipline
	/// tool. The plan Check and its Progression panel walk a plan in play order with it (is the tool given before the island
	/// that wants it?), and the generator's explorer's chest (GenFeatures) holds what its features want.
	/// Story-item locks (a keycard door, a cage and its bolt cutters, a generator and its part) are not here: they want a
	/// story item, ReadyPieces says which, and Check follows them as story items.
	/// </summary>
	public static class RaftFeatures
	{
		public const string MetalDetectorItem = "MetalDetector", ShovelItem = "Shovel";

		/// <summary>One of Raft's tools a feature wants.</summary>
		public class Tool
		{
			/// <summary>Raft's item (its unique name) and what players call it.</summary>
			public string Item, Label;
			/// <summary>Its blueprint: on one of Raft's story islands (raft_blueprints.txt), or given by an island.</summary>
			public string Blueprint;
			/// <summary>Learned at Raft's research table, in every world: no island has to give it. (No story island has its
			/// blueprint - raft_blueprints.txt.)</summary>
			public bool Researched;
		}

		/// <summary>A feature: a placed object that wants tools.</summary>
		public class Feature
		{
			public string Object, Label;
			public string[] Tools;
			/// <summary>Always an optional find (Raft's treasure loot, dirt): no quest step can point at it.</summary>
			public bool Optional;
		}

		public static readonly Tool[] Tools =
		{
			new Tool { Item = ContentCatalog.MacheteItem, Label = "machete", Blueprint = "Blueprint_Machete" },
			new Tool { Item = MetalDetectorItem, Label = "metal detector", Blueprint = "Blueprint_MetalDetector" },
			new Tool { Item = ShovelItem, Label = "shovel", Researched = true },
			new Tool { Item = ContentCatalog.ZiplineItem, Label = "zipline tool", Blueprint = "Blueprint_ZiplineTool", Researched = true },
		};

		public static readonly Feature[] Features =
		{
			// (the vines' own check, has|Machete - ObjectProps.Defaults - keeps the machete for the next vines)
			new Feature { Object = ContentCatalog.MacheteVines, Label = "vines", Tools = new[] { ContentCatalog.MacheteItem } },
			new Feature { Object = ContentCatalog.BuriedTreasure, Label = "buried treasure", Tools = new[] { MetalDetectorItem, ShovelItem }, Optional = true },
			new Feature { Object = "Pickup_Landmark_DirtPickup", Label = "dirt spots", Tools = new[] { ShovelItem }, Optional = true },
			new Feature { Object = "ZiplinePath_Landmark", Label = "a zipline", Tools = new[] { ContentCatalog.ZiplineItem } },
			new Feature { Object = "ZiplinePath", Label = "a zipline", Tools = new[] { ContentCatalog.ZiplineItem } },
		};

		/// <summary>
		/// Raft's story-island blueprints that build on others (TODO 4c "every blueprint and key item, in order"): the engine
		/// controls steer the motor wheels and the steering wheel, the fuel pipes feed from a fuel tank, the advanced biofuel
		/// extractor replaces the plain one, the water pipes run from a water tank. Raft's own order gives the bases first
		/// (Vasagatan before Caravan Town, Balboa before Temperance); a plan that gives one before its bases gets a Check
		/// warning (PlanChecker.CheckBlueprintOrder): players find a blueprint they can't use yet.
		/// </summary>
		public static readonly Dictionary<string, string[]> BuildsOn = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Blueprint_EngineControls", new[] { "Blueprint_MotorWheel", "Blueprint_SteeringWheel" } },
			{ "Blueprint_Pipe_Fuel", new[] { "Blueprint_Fueltank" } },
			{ "Blueprint_BiofuelExtractorAdvanced", new[] { "Blueprint_BiofuelExtractor" } },
			{ "Blueprint_Pipe_Water", new[] { "Blueprint_WaterTank" } },
		};

		/// <summary>The feature an object is, or null.</summary>
		public static Feature Of(string objectName) { return Features.FirstOrDefault(f => f.Object == objectName); }

		/// <summary>The tools an object wants as a feature (none: not a feature).</summary>
		public static string[] ToolsOf(string objectName) { Feature f = Of(objectName); return f != null ? f.Tools : new string[0]; }

		/// <summary>A tool by its item name, or null.</summary>
		public static Tool ToolOf(string item) { return Tools.FirstOrDefault(t => t.Item.Equals(item ?? "", StringComparison.OrdinalIgnoreCase)); }

		public static string LabelOf(string item) { Tool t = ToolOf(item); return t != null ? t.Label : ContentCatalog.ItemLabel(item); }

		static Regex given;

		/// <summary>The tools an island's settings give (an item list - loot, a give action, a quest reward - with the tool or
		/// its blueprint: "Machete*1", "Blueprint_Machete*1").</summary>
		public static IEnumerable<string> GivenIn(string text)
		{
			if (given == null)
				given = new Regex(@"(?<![A-Za-z0-9_])(?:Blueprint_)?(" + string.Join("|", Tools.Select(t => Regex.Escape(t.Item)).ToArray()) + @")\s*\*", RegexOptions.IgnoreCase);
			var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (Match m in given.Matches(text ?? "")) { Tool t = ToolOf(m.Groups[1].Value); if (t != null) found.Add(t.Item); }
			return found;
		}

		/// <summary>The tools a list of blueprints gives (one of Raft's story islands).</summary>
		public static IEnumerable<string> FromBlueprints(IEnumerable<string> blueprints)
		{
			var b = new HashSet<string>(blueprints, StringComparer.OrdinalIgnoreCase);
			return Tools.Where(t => t.Blueprint != null && b.Contains(t.Blueprint)).Select(t => t.Item);
		}
	}
}
