using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Dev tests of the player-feedback items (ROADMAP 3f, PF-D1 to PF-D5) that have no file of their own.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIWorldHint", docs: "Dev (ROADMAP PF-D5): the Esc > Custom Islands tip - due in a world while it was shown fewer than 3 times, counted when shown, never again once the window was opened; the New Game box's line names it. The PC's own state is put back")]
		public static void WorldHintCommand() { StartTest(WorldHintRoutine()); }

		static IEnumerator WorldHintRoutine()
		{
			bool ok = true;
			string kept = WorldWindowHint.Kept;
			try
			{
				WorldWindowHint.Restore(null);
				WorldWindowHint.OnWorldLoaded();
				Check(ref ok, WorldWindowHint.State == 0 && WorldWindowHint.Pending, "a PC that never saw it: the tip is due (state " + WorldWindowHint.State + ")");
				WorldWindowHint.Show();
				Check(ref ok, WorldWindowHint.State == 1, "shown once: counted (" + WorldWindowHint.State + ")");
				WorldWindowHint.Show(); WorldWindowHint.Show(); WorldWindowHint.Show();
				Check(ref ok, WorldWindowHint.State == WorldWindowHint.Times, "shown at most " + WorldWindowHint.Times + " times (" + WorldWindowHint.State + ")");
				WorldWindowHint.OnWorldLoaded();
				Check(ref ok, !WorldWindowHint.Pending, "after " + WorldWindowHint.Times + " worlds it is not due any more");
				WorldWindowHint.Restore(null);
				WorldWindowHint.Opened();
				WorldWindowHint.OnWorldLoaded();
				Check(ref ok, WorldWindowHint.State == -1 && !WorldWindowHint.Pending, "the window opened once: never again (state " + WorldWindowHint.State + ")");
				Check(ref ok, NewWorldOptions.WorldWindowLine.Contains("Esc > Custom Islands") && WorldWindowHint.Text.Contains("Esc > Custom Islands"), "the New Game box line and the tip name Esc > Custom Islands");
			}
			finally { WorldWindowHint.Restore(kept); }
			if (ok) Log("PASS: Esc > Custom Islands tip (PF-D5)"); else Fail("Esc > Custom Islands tip (PF-D5)");
			yield break;
		}

		[ConsoleCommand(name: "CIGenFuller", docs: "Dev, editor or menu (ROADMAP PF-D1): islands made while sailing - over 400 rolls about 70 % get one hostile animal and 50 % one friendly, each alone and never coming back; the same seed rolls the same settings; a generated island has more on land and under water than the old roll of the same island")]
		public static void GenFullerCommand() { StartTest(GenFullerRoutine()); }

		static IEnumerator GenFullerRoutine()
		{
			bool ok = true;
			yield return PlaceableCatalog.EnsureBuilt();
			int[] styles = { TerrainPainter.Tropical, TerrainPainter.Snowy, TerrainPainter.Desert, TerrainPainter.Forest, TerrainPainter.Volcanic };
			int hostile = 0, friendly = 0, n = 400;
			bool lone = true, same = true;
			for (int i = 0; i < n; i++)
			{
				IslandGenSettings a = IslandGenerator.RandomSettings(new System.Random(1000 + i), styles), b = IslandGenerator.RandomSettings(new System.Random(1000 + i), styles);
				if (a.ToText() != b.ToText()) same = false;
				if (a.Hostiles > 1 || a.Friendly > 1 || !a.LoneAnimals) lone = false;
				hostile += a.Hostiles; friendly += a.Friendly;
			}
			Check(ref ok, Mathf.Abs(hostile / (float)n - 0.7f) < 0.08f && Mathf.Abs(friendly / (float)n - 0.5f) < 0.08f && lone, "over " + n + " rolls: " + hostile + " with a hostile animal (70 %), " + friendly + " with a friendly one (50 %), never more than one each: " + lone);
			Check(ref ok, same, "the same seed rolls the same settings (a world seed gives the same island on every PC and after a reload)");

			// One island with both animals, generated as while sailing, against the old roll of the same island
			IslandGenSettings s = null;
			for (int i = 0; i < 200 && s == null; i++)
			{
				IslandGenSettings t = IslandGenerator.RandomSettings(new System.Random(5000 + i), new[] { TerrainPainter.Tropical });
				if (t.Hostiles == 1 && t.Friendly == 1 && t.Radius > 60f) s = t;
			}
			if (s == null) { Fail("no roll with both animals"); yield break; }
			IslandFile f = IslandGenerator.CreateFile(s, "ci-fuller");
			IslandGenSettings old = s.Copy();
			old.Trees = old.Bushes = old.Rocks = old.Harvest = old.BeachThings = -1f;
			old.Water = old.SeaRocks = old.SeaFinds = old.Sunken = -1f;
			old.Features = 0; old.Buildings = false; old.Hostiles = old.Friendly = 0; old.LoneAnimals = false; old.Gather = old.Shallows = 0f;
			IslandFile g = IslandGenerator.CreateFile(old, "ci-fuller-old");
			System.Func<IslandFile, bool, int> count = (file, land) => file.Objects.Count(o => !o.Name.StartsWith("Creature_") && (land ? o.Position.y > file.WaterLevel + 0.5f : o.Position.y < file.WaterLevel - 1f));
			int landNew = count(f, true), landOld = count(g, true), seaNew = count(f, false), seaOld = count(g, false);
			Check(ref ok, landNew > landOld && seaNew > seaOld, "seed " + s.Seed + " (" + s.Radius.ToString("F0") + " m): on land " + landOld + " -> " + landNew + ", under water " + seaOld + " -> " + seaNew + " objects");
			var animals = f.Objects.Where(o => o.Name.StartsWith("Creature_") && o.Position.y > f.WaterLevel).ToList();
			string Describe = string.Join(", ", animals.Select(o => o.Name + " x" + ObjectProps.Get(o.Props, ObjectProps.CreatureCount, "?") + " respawn " + ObjectProps.Get(o.Props, ObjectProps.CreatureRespawn, "yes")).ToArray());
			bool oneEach = animals.Count == 2 && animals.All(o => ObjectProps.Get(o.Props, ObjectProps.CreatureCount, "") == "1" && !ObjectProps.Respawns(o.Props))
				&& animals.Count(o => ContentCatalog.CreatureOf(o.Name) != null && ContentCatalog.CreatureOf(o.Name).Category == ContentCatalog.HostileCategory) == 1;
			Check(ref ok, oneEach, "its land animals: one hostile and one friendly, each alone and never back: " + Describe);
			if (ok) Log("PASS: fuller islands while sailing (PF-D1)"); else Fail("fuller islands while sailing (PF-D1)");
		}
	}
}
