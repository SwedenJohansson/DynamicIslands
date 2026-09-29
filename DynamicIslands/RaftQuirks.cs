using HarmonyLib;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's own objects that throw when they are used outside a world. The editor loads Raft's island scenes to offer
	/// their objects; a music zone in them (AreaZone_MusicActivation) looks up its quest when it is switched on - there is
	/// no quest tracker outside a world, so it threw a NullReferenceException, red in the console of every player who
	/// opened the editor with such objects. Outside a world it now does nothing; in a world it works as Raft's.
	/// </summary>
	[HarmonyPatch(typeof(AreaZone_MusicActivation), "OnEnable")]
	static class MusicZoneOutsideWorld
	{
		static bool Prefix() { return LoadSceneManager.IsGameSceneLoaded; }
	}

	/// <summary>
	/// Raft's boats, barrels and containers cut the sea out of their insides (UltimateWater's WaterVolumeSubtract). The
	/// editor has no sea of Raft's: each such object logged "Water reference is null" in red when placed (145 lines in
	/// one test run). Without water there is nothing to cut out.
	/// </summary>
	[HarmonyPatch(typeof(UltimateWater.WaterVolumeSubtract), "Register")]
	static class WaterCutoutWithoutWater
	{
		static bool Prefix(UltimateWater.Water water) { return water != null; }
	}
}
