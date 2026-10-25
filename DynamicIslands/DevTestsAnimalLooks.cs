using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// Rare animal looks in a world (_ProjectDocs/plans/ANIMAL_LOOKS_TESTS.md B, C2, E, F4, host): for Normal, More and
	/// Lots a test island with 32 cluckers, goats and llamas; the looks Raft's randomizer gave them are counted (Normal:
	/// only Raft's own; More/Lots: the new looks 3-5 too and more rare ones), the three new looks made without a
	/// failure; then the tames (Raft's Captured hook) fill the collection, the journal's x/6 and the world file's lines.
	/// Pictures at Lots: shot_looks_&lt;animal&gt;.png.
	/// </summary>
	public static partial class DevTests
	{
		const string AlIsland = "cilooks";

		[ConsoleCommand(name: "CIAnimalLooksRun", docs: "Dev, world (host): rare animal looks at Normal, More and Lots - 32 of each farm animal spawned per level, the looks counted, the new looks made, tames counted in the journal and the world file; pictures shot_looks_<animal>.png")]
		public static void AnimalLooksRunCommand(string[] args) { StartTest(AnimalLooksRunRoutine()); }

		static IEnumerator AnimalLooksRunRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("animal looks: in a world, as the host"); yield break; }
			bool ok = true;
			int level0 = AnimalLooks.Current;
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Creature_Chicken", "Creature_Goat", "Creature_Llama" });

			// F4: the command's polite refusals
			AnimalLooks.AnimalLooksCommand(new[] { "5" });
			AnimalLooks.AnimalLooksCommand(new[] { "foo" });
			Check(ref ok, AnimalLooks.Current == level0, "F4: AnimalLooks 5 / foo leave the level at " + AnimalLooks.Describe(level0));

			var lots = new Dictionary<int, List<AI_NetworkBehaviour>>();
			for (int level = AnimalLooks.Normal; level <= AnimalLooks.Lots; level++)
			{
				AnimalLooks.Set(level);
				IslandFile f = ScIsland(AlIsland, "Looks Run");
				int seed = 1;
				var names = new[] { "Creature_Chicken", "Creature_Goat", "Creature_Llama" };
				for (int a = 0; a < 3; a++)
					for (int s = 0; s < 4; s++)
						f.Objects.Add(ScObj(names[a], ScDry(f, new Vector2(-24f + a * 24f, -18f + s * 12f), seed++), ObjectProps.CreatureCount, "8", ObjectProps.CreatureRespawn, "0", BehaviourProps.Name, "L" + a + s));
				IslandWorldState.Entry e = null;
				yield return RfSpawn(f, AlIsland, x => e = x);
				if (e == null) { Check(ref ok, false, "the looks island at " + AnimalLooks.Describe(level)); break; }
				yield return new WaitForSeconds(4f);

				int newTotal = 0;
				for (int a = 0; a < 3; a++)
				{
					List<AI_NetworkBehaviour> ais = e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true)
						.Where(p => p.Kind != null && p.Kind.Type == AnimalLooks.Animals[a]).SelectMany(p => p.Spawned).Where(x => x != null).ToList();
					var counts = new int[6];
					int unknown = 0;
					foreach (AI_NetworkBehaviour ai in ais)
					{
						var d = ai as AI_NetworkBehaviour_Domestic;
						int look = d != null && d.rareMaterialScript != null && d.rareMaterialScript.randomizer != null ? (int)d.rareMaterialScript.randomizer.lastRandomizedIndex : -1;
						if (look >= 0 && look < 6) counts[look]++; else unknown++;
					}
					int rare = counts.Skip(1).Sum(), news = counts.Skip(3).Sum();
					newTotal += news;
					float share = ais.Count > 0 ? rare / (float)ais.Count : 0f;
					string line = AnimalLooks.Describe(level) + " " + AnimalLooks.AnimalNames[a] + ": " + ais.Count + " spawned, looks " + string.Join("/", counts.Select(c => c.ToString()).ToArray()) + (unknown > 0 ? ", unread " + unknown : "") + ", rare " + (share * 100f).ToString("F0") + "%";
					bool good = ais.Count >= 24 && unknown == 0;
					if (level == AnimalLooks.Normal) good &= news == 0 && share <= 0.35f;
					else if (level == AnimalLooks.More) good &= share >= 0.1f && share <= 0.6f;
					else good &= share >= 0.25f && share <= 0.8f;
					Check(ref ok, good, "B: " + line);
					if (level == AnimalLooks.Lots) lots[a] = ais;
				}
				if (level > AnimalLooks.Normal) Check(ref ok, newTotal > 0, "B: " + AnimalLooks.Describe(level) + " gives the new looks 3-5 (" + newTotal + ")");
				if (level < AnimalLooks.Lots) { IslandWorldState.Remove(AlIsland); yield return new WaitForSeconds(2f); }
			}

			// The new looks were made once per animal, none failed
			var made = Traverse.Create(typeof(AnimalLooks)).Field("made").GetValue() as IDictionary;
			var failed = Traverse.Create(typeof(AnimalLooks)).Field("failed").GetValue() as ICollection;
			Check(ref ok, made != null && made.Count >= 3 && (failed == null || failed.Count == 0), "B: new looks made for " + (made != null ? made.Count : 0) + " animals, failed " + (failed != null ? failed.Count : -1));

			// Pictures at Lots: the herd of each animal
			for (int a = 0; a < 3 && lots.ContainsKey(a); a++)
			{
				AI_NetworkBehaviour mid = lots[a].FirstOrDefault(x => x != null);
				if (mid != null) yield return ShotOf(player, mid.transform, "looks_" + AnimalLooks.AnimalNames[a].ToLowerInvariant(), 5f);
			}

			// E: tames fill the collection (Raft's Captured hook's own handler), x/6 in the journal, the world file's lines
			int caught0 = AnimalLooks.Caught;
			foreach (var kv in lots)
				foreach (AI_NetworkBehaviour ai in kv.Value)
					if (ai is AI_NetworkBehaviour_Domestic) AnimalLooks.OnCaptured((AI_NetworkBehaviour_Domestic)ai);
			int seen = 0, kinds = 0;
			for (int a = 0; a < 3; a++)
			{
				var lookSet = new HashSet<int>(lots.ContainsKey(a) ? lots[a].OfType<AI_NetworkBehaviour_Domestic>().Where(d => d.rareMaterialScript != null && d.rareMaterialScript.randomizer != null).Select(d => (int)d.rareMaterialScript.randomizer.lastRandomizedIndex) : new int[0]);
				seen += lookSet.Count;
				kinds += AnimalLooks.CountOf(a);
				Check(ref ok, AnimalLooks.CountOf(a) == lookSet.Count, "E: " + AnimalLooks.AnimalNames[a] + " tamed " + AnimalLooks.CountOf(a) + "/6, looks in the herd " + lookSet.Count);
			}
			string journal = AnimalLooks.JournalText();
			Check(ref ok, caught0 == 0 && journal.Contains("Animal looks tamed") && journal.Contains("/6"), "E: the journal lists the tamed looks: " + journal.Replace("\n", " | "));
			string lines = string.Join(" ", AnimalLooks.WriteLines().ToArray());
			Check(ref ok, lines.Contains("@rarelooks=2") && lines.Contains("@lookscaught=" + AnimalLooks.Caught), "C2: the world file gets " + lines);
			int before = AnimalLooks.Caught;
			foreach (var kv in lots) foreach (AI_NetworkBehaviour ai in kv.Value) if (ai is AI_NetworkBehaviour_Domestic) AnimalLooks.OnCaptured((AI_NetworkBehaviour_Domestic)ai);
			Check(ref ok, AnimalLooks.Caught == before, "E: repeat tames change nothing");

			IslandWorldState.Remove(AlIsland);
			AnimalLooks.Set(level0);
			if (ok) Log("PASS: animal looks run"); else Fail("animal looks run");
		}
	}
}
