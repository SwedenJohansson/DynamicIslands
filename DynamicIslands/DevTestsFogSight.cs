using System;
using System.Collections;
using System.Collections.Generic;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIFogSight", docs: "Dev, in game (host): Binoculars see through fog (PF9). Checks the factors, the Marine binoculars upgrade and that plan steps count it as binoculars, then in the world: the haze closer, binoculars/telescope/marine further, a fog bank kept, the night lift with Marine binoculars, and Raft's fog back with the option off.")]
		public static void FogSightCommand(string[] args) { StartTest(FogSightRoutine()); }

		static IEnumerator FogSightRoutine()
		{
			bool ok = true;
			// Pure parts
			Check(ref ok, FogSight.FactorFor("") < 1f && FogSight.FactorFor("binoculars") > 1f && FogSight.FactorFor("telescope") > FogSight.FactorFor("binoculars") && FogSight.FactorFor("marine") > FogSight.FactorFor("telescope"), "haze < 1 < binoculars < telescope < marine");
			Check(ref ok, FogSight.LookOf("Binoculars") == "binoculars" && FogSight.LookOf(ExtraUpgrades.TelescopeName) == "telescope" && FogSight.LookOf(ExtraUpgrades.MarineName) == "marine" && FogSight.LookOf("Hammer") == "" && FogSight.LookOf(null) == "", "what each item looks through");
			Check(ref ok, Mathf.Abs(FogSight.WithBank(0.7f, 1f) - 1f) < 0.001f && Mathf.Abs(FogSight.WithBank(0.7f, 0f) - 0.7f) < 0.001f && Mathf.Abs(FogSight.WithBank(1.5f, 1f) - 1.5f) < 0.001f, "the haze fades out in a fog bank, the binoculars still see further");
			ExtraUpgrades.Upgrade m = ExtraUpgrades.Find(ExtraUpgrades.MarineName);
			Check(ref ok, m != null && m.Index == ExtraUpgrades.MarineIndex && m.BaseName == "Binoculars" && m.ExtraCostItem == "TitaniumIngot", "Marine binoculars: an upgrade of the binoculars, made with titanium");
			Check(ref ok, GapChecks.NameMatches(ExtraUpgrades.MarineName, "Binoculars") && GapChecks.NameMatches(ExtraUpgrades.TelescopeName, "Binoculars"), "a holds|Binoculars check takes the telescope and Marine binoculars");
			global::DynamicIslands.Editor.RaftFeatures.Tool tool = global::DynamicIslands.Editor.RaftFeatures.ToolOf(ExtraUpgrades.MarineName);
			Check(ref ok, tool != null && tool.Item == global::DynamicIslands.Editor.RaftFeatures.BinocularsItem && global::DynamicIslands.Editor.RaftFeatures.ToolOf(ExtraUpgrades.TelescopeName) == tool, "plan checker: the telescope and Marine binoculars are the binoculars tool");

			if (CustomIslandSpawner.RaftPosition == null || !Raft_Network.IsHost || Camera.main == null) { Fail("fog sight: run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			OnRaftCommand();
			yield return new WaitForSeconds(1.5f);
			if (Camera.main == null || Camera.main.transform.position.y < -0.6f) { Fail("fog sight: the camera is under water"); yield break; }
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			try
			{
				FogBanks.Reset();
				FogBanks.TestTarget = 0f;
				FogSight.Reset();
				SetHour(12f);
				yield return new WaitForSeconds(1.5f);
				float raft = FogSight.RaftDistance;
				Check(ref ok, Mathf.Abs(FogSight.ShownDistance - raft) < 1f, "option off: Raft's own fog (" + raft.ToString("F0") + ")");

				var set = new HashSet<string>(optionsBefore); set.Add(WorldOptions.FogSight); WorldOptions.Set(set);
				FogSight.TestInstant = true;
				foreach (string look in new[] { "", "binoculars", "telescope", "marine" })
				{
					FogSight.TestLook = look;
					yield return new WaitForSeconds(1f);
					float f = FogSight.FactorFor(look), want = FogSight.RaftDistance * f;
					Check(ref ok, Mathf.Abs(FogSight.Factor - f) < 0.001f && Mathf.Abs(FogSight.ShownDistance - want) < Mathf.Max(1f, want * 0.02f),
						(look == "" ? "the eyes" : look) + ": fog at " + FogSight.ShownDistance.ToString("F0") + " (Raft's " + FogSight.RaftDistance.ToString("F0") + " x " + f.ToString("F2") + ")");
				}
				// Eased, not jumping: from marine back to the eyes takes a few frames
				FogSight.TestInstant = false;
				FogSight.TestLook = "";
				yield return null; yield return null;
				Check(ref ok, FogSight.Factor > FogSight.HazeFactor + 0.01f, "lowering the binoculars eases back (" + FogSight.Factor.ToString("F2") + ")");
				yield return new WaitForSeconds(1f);
				Check(ref ok, Mathf.Abs(FogSight.Factor - FogSight.HazeFactor) < 0.001f, "and settles on the haze");

				// A fog bank: the haze doesn't thin it further, binoculars still see a little further
				FogSight.TestInstant = true;
				FogBanks.TestVisibility = 60f; FogBanks.TestInstant = true; FogBanks.TestTarget = 1f;
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, FogBanks.Weight > 0.99f && Mathf.Abs(FogSight.ShownDistance - FogSight.RaftDistance) < 1f, "in a fog bank the eyes see the bank's fog (" + FogSight.ShownDistance.ToString("F0") + ")");
				FogSight.TestLook = "telescope";
				yield return new WaitForSeconds(1f);
				Check(ref ok, FogSight.ShownDistance > FogSight.RaftDistance * 1.4f, "the telescope sees further into the bank (" + FogSight.ShownDistance.ToString("F0") + " of " + FogSight.RaftDistance.ToString("F0") + ")");
				FogBanks.TestTarget = 0f;
				yield return new WaitForSeconds(1.5f);

				// Night: Marine binoculars lift the light
				SetHour(0.5f);
				FogSight.TestLook = "binoculars";
				yield return new WaitForSeconds(2f);
				float dark = RenderSettings.ambientIntensity;
				FogSight.TestLook = "marine";
				yield return new WaitForSeconds(1f);
				float lit = RenderSettings.ambientIntensity;
				Check(ref ok, FogSight.NightLift > 0.99f && (dark >= FogSight.NightAmbient || lit > dark + 0.05f), "Marine binoculars lighten the night (ambient " + dark.ToString("F2") + " -> " + lit.ToString("F2") + ")");
				FogSight.TestLook = "";
				yield return new WaitForSeconds(1f);
				Check(ref ok, FogSight.NightLift == 0f && Mathf.Abs(RenderSettings.ambientIntensity - dark) < 0.05f, "lowered: Raft's own night light (" + RenderSettings.ambientIntensity.ToString("F2") + ")");

				// Option off: everything back
				WorldOptions.Set(optionsBefore);
				SetHour(12f);
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, FogSight.Factor == 1f && Mathf.Abs(FogSight.ShownDistance - FogSight.RaftDistance) < 1f, "option off: Raft's fog back (" + FogSight.ShownDistance.ToString("F0") + ")");
			}
			finally { FogSight.Reset(); FogBanks.Reset(); WorldOptions.Set(optionsBefore); }
			if (ok) Log("PASS: fog sight"); else Fail("fog sight");
		}
	}
}
