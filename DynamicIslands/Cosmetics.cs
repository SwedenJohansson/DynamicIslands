using System;
using System.Collections.Generic;
using System.Linq;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Cosmetic rewards (PF7): wearable hats and placeable trophies, new items for quest chests, quest rewards and "give"
	/// actions. Each is a tinted copy of one of Raft's own hats or trophies (its model, no new art), made by ExtraUpgrades
	/// exactly as an upgrade is (ExtraUpgrades.CosmeticItems): its own fixed item index, registered with RAPI when the mod
	/// starts, so worlds save them like Raft's items. A hat gets its own copy of the base hat's Equipment (worn on the head
	/// slot, the shared model tinted while worn); a trophy is a placeable block (tinted, with the upgrades' glints).
	///   - Never crafted or learned (not in ExtraUpgrades.Registered, so the upgrade table, crafting and the upgrade tests
	///     don't see them), whatever the world settings: only chests and rewards hand them out.
	///   - In the editor they are in the item picker like any item, and the chest loot preset "Cosmetics" holds a hat and a
	///     trophy.
	///   - Item indexes 29460-29466 (29445-29459 are left for the upgrades); they must never change.
	/// </summary>
	public static class Cosmetics
	{
		public const int FirstIndex = 29460;
		public const string AdmiralHat = "DI_HatAdmiral", CorsairHat = "DI_HatCorsair", ExplorerHat = "DI_HatExplorer", ChieftainMask = "DI_HatChieftain";
		public const string GoldenShark = "DI_TrophyGoldenShark", SilverRhino = "DI_TrophySilverRhino", JadeTiki = "DI_TrophyJadeTiki";
		/// <summary>The chest loot preset (ContentCatalog.LootPresets) with a hat and a trophy.</summary>
		public static readonly string[] PresetLoot = { AdmiralHat + "*1", GoldenShark + "*1" };

		static readonly Color Gold = new Color(1f, 0.8f, 0.25f), Crimson = new Color(0.85f, 0.15f, 0.2f), Jade = new Color(0.2f, 0.75f, 0.5f),
			Amber = new Color(0.95f, 0.55f, 0.15f), Silver = new Color(0.85f, 0.88f, 0.95f);

		/// <summary>A worn cosmetic: a copy of its base hat's Equipment, no stats of its own (ExtraUpgrades.AddEquipment).</summary>
		static void Hat(Equipment e) { }

		static ExtraUpgrades.Upgrade Wear(int i, string name, string baseName, string display, Color tint, string text)
		{
			return new ExtraUpgrades.Upgrade { Index = FirstIndex + i, Name = name, BaseName = baseName, Display = display, Tint = tint, CostFactor = 1, Worn = Hat, Description = text };
		}

		static ExtraUpgrades.Upgrade Place(int i, string name, string baseName, string display, Color tint, bool whiten, string text)
		{
			return new ExtraUpgrades.Upgrade { Index = FirstIndex + i, Name = name, BaseName = baseName, Display = display, Tint = tint, Whiten = whiten, CostFactor = 1, Description = text };
		}

		/// <summary>The cosmetic items (ExtraUpgrades.CosmeticItems makes them once and marks them Cosmetic).</summary>
		internal static List<ExtraUpgrades.Upgrade> Defs()
		{
			return new List<ExtraUpgrades.Upgrade>
			{
				Wear(0, AdmiralHat, "Hat_Captain", "Admiral's hat", Gold, "A golden captain's hat, given for a quest well done. Worn on the head; just for looks."),
				Wear(1, CorsairHat, "Hat_Pirate", "Corsair's hat", Crimson, "A crimson pirate hat from a quest reward. Worn on the head; just for looks."),
				Wear(2, ExplorerHat, "Hat_Fishing", "Explorer's hat", Jade, "A jade explorer's hat from a quest reward. Worn on the head; just for looks."),
				Wear(3, ChieftainMask, "Hat_Tiki", "Chieftain's mask", Amber, "An amber tiki mask from a quest reward. Worn on the head; just for looks."),
				Place(4, GoldenShark, "Placeable_SharkTrophy", "Golden shark trophy", Gold, false, "A golden shark trophy, a quest reward. Place it on the raft to show it off."),
				Place(5, SilverRhino, "Placeable_RhinoSharkTrophy", "Silver rhino shark trophy", Silver, true, "A silver rhino shark trophy, a quest reward. Place it on the raft to show it off."),
				Place(6, JadeTiki, "Placeable_TikiPole_Complete", "Jade tiki pole", Jade, false, "A jade tiki pole, a quest reward. Place it on the raft to show it off."),
			};
		}

		public static IEnumerable<ExtraUpgrades.Upgrade> All { get { return ExtraUpgrades.CosmeticItems; } }
		public static IEnumerable<ExtraUpgrades.Upgrade> Hats { get { return All.Where(u => u.Worn != null); } }
		public static IEnumerable<ExtraUpgrades.Upgrade> Trophies { get { return All.Where(u => u.Worn == null); } }

		public static bool IsCosmetic(string uniqueName) { return !string.IsNullOrEmpty(uniqueName) && All.Any(u => u.Name == uniqueName); }

		[ConsoleCommand(name: "Cosmetics", docs: "Cosmetic rewards (hats and trophies for quest chests and rewards): Cosmetics = the list and whether each is registered; Cosmetics give = one of each to this player - tests")]
		public static void CosmeticsCommand(string[] args)
		{
			foreach (ExtraUpgrades.Upgrade u in All)
				Debug.Log("[CUSTOM ISLANDS] Cosmetic " + u.Name + " (" + u.Index + "): " + u.Display + " from " + u.BaseName + (u.Item != null ? "" : " - NOT registered"));
			if (args != null && args.Length > 0 && args[0] == "give")
				Debug.Log("[CUSTOM ISLANDS] Cosmetics given: " + string.Join(", ", TriggerZone.Give(All.Where(u => u.Item != null).Select(u => new KeyValuePair<string, int>(u.Name, 1))).ToArray()));
		}
	}
}
