using System;
using System.Globalization;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using UnityEngine.AzureSky;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world option "Binoculars see through fog" (WorldOptions.FogSight, PF9). Off by default.
	///   - Normally the sea's haze (Raft's sky fog) comes HazeFactor (~30 %) closer than in Raft.
	///   - While this player looks through binoculars the fog lies BinocularsFactor (~20 %) further than Raft's own, through the
	///     telescope (DI_Telescope) TelescopeFactor (~50 %), through Marine binoculars (DI_MarineBinoculars, an extra upgrade)
	///     MarineFactor. Marine binoculars also lighten the view at dusk and at night (MarineNight: Raft's ambient light lifted).
	///   - It multiplies the fog distance Raft's sky has right now (its weather's own fog, and a fog bank's), lent just while
	///     the sky hands its values to the shaders (FogBanksSkyPatch calls Lend/Return around FogBanks'), so Raft's weather
	///     blending never sees ours and everything is back when the binoculars are lowered. In a fog bank the haze factor
	///     fades out (the bank is thick enough). Under water nothing changes.
	///   - Local only: each player sees through their own binoculars (what this machine's player holds and raises). Nothing is
	///     saved or sent; the option itself comes with the world's options (kind 17).
	///   - The factor eases (Speed per second), so raising and lowering the binoculars doesn't jump.
	/// </summary>
	public static class FogSight
	{
		public const float HazeFactor = 0.7f, BinocularsFactor = 1.2f, TelescopeFactor = 1.5f, MarineFactor = 1.8f, Speed = 3f;
		/// <summary>Raft's ambient light is lifted to at least this (times the night lift) with Marine binoculars raised.</summary>
		public const float NightAmbient = 1f, NightExposure = 1.5f;

		/// <summary>The factor on Raft's fog distance now (eased), and the one it moves to.</summary>
		public static float Factor { get; private set; } = 1f;
		public static float Target { get; private set; } = 1f;
		/// <summary>How much of the Marine binoculars' night lift is on now (0..1, eased).</summary>
		public static float NightLift { get; private set; }
		/// <summary>What this player looks through now: "", "binoculars", "telescope" or "marine".</summary>
		public static string Looking { get; private set; } = "";
		/// <summary>Raft's fog distance and the one shown, the last time the sky set its shaders.</summary>
		public static float RaftDistance { get; private set; }
		public static float ShownDistance { get; private set; }

		/// <summary>Tests: what this machine looks through (null = what the player really does); ease at once.</summary>
		internal static string TestLook;
		internal static bool TestInstant;

		static float lentDistance, lentExposure;
		static bool lent;

		public static bool On { get { return WorldOptions.On(WorldOptions.FogSight); } }

		public static void Reset() { TestLook = null; TestInstant = false; Factor = 1f; Target = 1f; NightLift = 0f; Looking = ""; lent = false; }

		/// <summary>The factor for what is looked through (the option on).</summary>
		public static float FactorFor(string look)
		{
			switch (look)
			{
				case "binoculars": return BinocularsFactor;
				case "telescope": return TelescopeFactor;
				case "marine": return MarineFactor;
				default: return HazeFactor;
			}
		}

		/// <summary>What an item is to look through: "binoculars", "telescope", "marine", or "" for anything else.</summary>
		public static string LookOf(string item)
		{
			if (string.IsNullOrEmpty(item)) return "";
			if (item == ExtraUpgrades.MarineName) return "marine";
			if (item == ExtraUpgrades.TelescopeName) return "telescope";
			if (item == RaftFeatures.BinocularsItem) return "binoculars";
			return "";
		}

		/// <summary>The factor with a fog bank's weight: the haze (a factor below 1) fades out in a bank.</summary>
		public static float WithBank(float factor, float bankWeight)
		{
			return Mathf.Lerp(factor, Mathf.Max(1f, factor), Mathf.Clamp01(bankWeight));
		}

		/// <summary>What the local player looks through right now: the held item, and its binoculars raised.</summary>
		static string LookNow()
		{
			if (TestLook != null) return TestLook;
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null || p.Inventory == null) return "";
			ItemInstance inst = p.Inventory.GetSelectedHotbarItem();
			Item_Base held = inst != null ? inst.baseItem : null;
			string look = LookOf(held != null ? held.UniqueName : null);
			if (look == "") return "";
			foreach (Binoculars b in p.GetComponentsInChildren<Binoculars>(false))
				if (Traverse.Create(b).Field("active").GetValue<bool>()) return look;
			return "";
		}

		public static void Tick()
		{
			if (!On)
			{
				Looking = ""; Target = 1f;
				Factor = 1f; NightLift = 0f;
				return;
			}
			Looking = LookNow();
			Target = FactorFor(Looking);
			float step = TestInstant ? 100f : Time.unscaledDeltaTime * Speed;
			Factor = Mathf.MoveTowards(Factor, Target, step);
			NightLift = Mathf.MoveTowards(NightLift, Looking == "marine" ? 1f : 0f, step);
		}

		static bool Underwater
		{
			get
			{
				Camera c = Camera.main;
				return c != null && c.transform.position.y < -0.6f;
			}
		}

		/// <summary>FogBanksSkyPatch, after FogBanks lent its fog: ours on top, kept to put back.</summary>
		internal static void Lend(AzureSkyController s)
		{
			lent = false;
			RaftDistance = s.fogDistance;
			ShownDistance = s.fogDistance;
			if (!On || Underwater) return;
			float f = WithBank(Factor, FogBanks.Weight);
			bool dark = NightLift > 0.001f;
			if (Mathf.Abs(f - 1f) < 0.001f && !dark) return;
			lent = true;
			lentDistance = s.fogDistance;
			lentExposure = s.exposure;
			s.fogDistance = Mathf.Max(1f, s.fogDistance * f);
			if (dark) s.exposure = s.exposure * Mathf.Lerp(1f, NightExposure, NightLift * Darkness(s));
			ShownDistance = s.fogDistance;
		}

		/// <summary>After the hand-over, before FogBanks puts Raft's values back.</summary>
		internal static void Return(AzureSkyController s)
		{
			if (!lent) return;
			lent = false;
			s.fogDistance = lentDistance;
			s.exposure = lentExposure;
		}

		/// <summary>How dark Raft's light is now: 0 by day, 1 at night (from its ambient intensity).</summary>
		static float Darkness(AzureSkyController s) { return Mathf.InverseLerp(NightAmbient, NightAmbient * 0.25f, s.ambientIntensity); }

		/// <summary>FogSightNightPatch, after Raft's sky set the ambient light this frame: lifted with Marine binoculars.</summary>
		internal static void LiftNight(AzureSkyController s)
		{
			if (NightLift <= 0.001f || !On) return;
			float now = RenderSettings.ambientIntensity;
			if (now < NightAmbient) RenderSettings.ambientIntensity = Mathf.Lerp(now, NightAmbient, NightLift);
		}

		[ConsoleCommand(name: "FogSight", docs: "The world option Binoculars see through fog: FogSight = what this player looks through and the fog now; FogSight test none|binoculars|telescope|marine|auto = look through that on this machine (auto = what the player really does) - tests")]
		public static void FogSightCommand(string[] args)
		{
			if (args != null && args.Length > 1 && args[0] == "test")
			{
				string a = args[1].ToLowerInvariant();
				TestLook = a == "auto" ? null : a == "none" ? "" : a;
				Debug.Log("[CUSTOM ISLANDS] Fog sight test: " + (TestLook == null ? "what the player does" : TestLook == "" ? "not looking" : TestLook));
			}
			Debug.Log("[CUSTOM ISLANDS] Fog sight: " + (On ? "on" : "off") + ", looking " + (Looking == "" ? "with the eyes" : "through " + Looking) +
				", factor " + Factor.ToString("0.00", CultureInfo.InvariantCulture) + " (to " + Target.ToString("0.00", CultureInfo.InvariantCulture) + ")" +
				", night lift " + NightLift.ToString("0.00", CultureInfo.InvariantCulture) +
				", sky fog distance " + RaftDistance.ToString("0", CultureInfo.InvariantCulture) + " -> " + ShownDistance.ToString("0", CultureInfo.InvariantCulture) +
				(FogBanks.Weight > 0.001f ? ", in a fog bank (" + FogBanks.Weight.ToString("0.00", CultureInfo.InvariantCulture) + ")" : ""));
		}
	}

	/// <summary>Binoculars see through fog: Marine binoculars lift Raft's ambient light at night (see FogSight).</summary>
	[HarmonyPatch(typeof(AzureSkyController), "Update")]
	static class FogSightNightPatch
	{
		static void Postfix(AzureSkyController __instance) { FogSight.LiftNight(__instance); }
	}
}
