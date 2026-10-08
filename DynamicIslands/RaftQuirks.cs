using HarmonyLib;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's own objects that throw when they are used outside a world. The editor loads Raft's island scenes to offer
	/// their objects; a music zone in them (AreaZone_MusicActivation) looks up its quest when it is switched on - there is
	/// no quest tracker outside a world, so it threw a NullReferenceException, red in the console of every player who
	/// opened the editor with such objects. Raft's code always runs; only its error outside a world is dropped (it was
	/// skipped outside a world, and an island switched on while a world was still loading lost its music - CA25).
	/// </summary>
	[HarmonyPatch(typeof(AreaZone_MusicActivation), "OnEnable")]
	static class MusicZoneOutsideWorld
	{
		static System.Exception Finalizer(System.Exception __exception) { return LoadSceneManager.IsGameSceneLoaded ? __exception : null; }
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
