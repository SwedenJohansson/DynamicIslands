using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.AI;

namespace DynamicIslands
{
	/// <summary>Tests of the world randomizer (WorldRandomizer): Raft's own islands, animals and sharks in a normal world.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIProbeLandmarks", docs: "Dev, world: lists Raft's spawned islands near the raft - their items, animal spawners and NavMesh - and every animal in the world, for the randomizer. CIProbeLandmarks [max distance, default 1500]")]
		public static void ProbeLandmarksCommand(string[] args)
		{
			float max = 1500f;
			if (args != null && args.Length > 0) float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out max);
			try { ProbeLandmarks(max); }
			catch (Exception e) { Fail("probe: " + e); }
		}

		#region The randomizer's rules (anywhere)

		[ConsoleCommand(name: "CIRandomizerUnit", docs: "Dev, anywhere: the world randomizer's settings text, levels, and animal colours and alphas: how often, the same for the same animal, different per world seed")]
		public static void RandomizerUnitCommand()
		{
			RandomizerSettings keep = WorldRandomizer.Current;
			bool ok = true;
			try
			{
				// Settings survive their text (world file, network message)
				RandomizerSettings s = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 424242 };
				s.Disabled.Add(RandomizerSettings.Alphas); s.Disabled.Add(RandomizerSettings.Bosses);
				RandomizerSettings back = RandomizerSettings.Decode(s.Encode());
				Check(ref ok, back.Level == s.Level && back.Seed == s.Seed && back.Disabled.SetEquals(s.Disabled), "settings text round trip: '" + s.Encode() + "'");
				Check(ref ok, RandomizerSettings.Decode("level=off").Level == RandomizerSettings.Off && !RandomizerSettings.Decode("").On && RandomizerSettings.Decode("level=Light;seed=5").Level == RandomizerSettings.Light, "levels by name, empty = off");
				Check(ref ok, !back.Has(RandomizerSettings.Alphas) && back.Has(RandomizerSettings.Colours) && !new RandomizerSettings().Has(RandomizerSettings.Colours), "parts on and off; nothing when off");
				Check(ref ok, back.Describe().StartsWith("Wild: ") && back.Describe().Contains("colours") && !back.Describe().Contains("alphas"), "description: " + back.Describe());
				Log("Hash(1,2,3) = " + WorldRandomizer.Hash(1, 2, 3) + " (integer maths only: the same on every machine)");

				// How often animals change, per level (the chances in VariantOf)
				foreach (int level in new[] { RandomizerSettings.Light, RandomizerSettings.Normal, RandomizerSettings.Wild })
				{
					WorldRandomizer.Current = new RandomizerSettings { Level = level, Seed = 777 };
					int n = 4000, coloured = 0, alpha = 0, bruce = 0, gold = 0;
					for (uint i = 1; i <= n; i++)
					{
						WorldRandomizer.Variant v = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Boar, i);
						if (v != null && v.Alpha) alpha++; else if (v != null) coloured++;
						WorldRandomizer.Variant sv = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Shark, i);
						if (sv != null && sv.Alpha) bruce++;
						if (sv != null && sv.Name == "gold") gold++;
					}
					float c = WorldRandomizer.Current.Pick(0.15f, 0.3f, 0.55f), a = WorldRandomizer.Current.Pick(0.03f, 0.06f, 0.12f), b = WorldRandomizer.Current.Pick(0.06f, 0.12f, 0.22f);
					float fc = coloured / (float)n, fa = alpha / (float)n, fb = bruce / (float)n;
					Check(ref ok, Mathf.Abs(fa - a) < 0.025f && Mathf.Abs(fc - c * (1f - a)) < 0.04f && Mathf.Abs(fb - b) < 0.03f,
						RandomizerSettings.LevelNames[level] + ": " + (fc * 100f).ToString("F0") + "% of warthogs coloured, " + (fa * 100f).ToString("F1") + "% alphas, " + (fb * 100f).ToString("F1") + "% of sharks a big Bruce, " + gold + " golden sharks");
				}
				// Never for bosses, people, bugs
				WorldRandomizer.Current = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 5 };
				bool none = true;
				foreach (AI_NetworkBehaviourType t in new[] { AI_NetworkBehaviourType.Boss_Varuna, AI_NetworkBehaviourType.MamaBear, AI_NetworkBehaviourType.HyenaBoss, AI_NetworkBehaviourType.NPC_Johan, AI_NetworkBehaviourType.ButlerBot, AI_NetworkBehaviourType.BugSwarm_Bee, AI_NetworkBehaviourType.Whale })
					for (uint i = 1; i < 300; i++) if (WorldRandomizer.VariantOf(t, i) != null) none = false;
				Check(ref none, none, "bosses, people, bugs and the whale keep Raft's looks");
				ok &= none;
				// The same animal looks the same every time; another world seed gives other animals other looks
				int same = 0, differs = 0;
				for (uint i = 1; i <= 500; i++)
				{
					WorldRandomizer.Variant v1 = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Goat, i), v2 = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Goat, i);
					if ((v1 == null ? "" : v1.Name) == (v2 == null ? "" : v2.Name)) same++;
				}
				WorldRandomizer.Current = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 6 };
				var other = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 5 };
				for (uint i = 1; i <= 500; i++)
				{
					WorldRandomizer.Variant v1 = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Goat, i);
					WorldRandomizer.Current = other;
					WorldRandomizer.Variant v2 = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Goat, i);
					WorldRandomizer.Current = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 6 };
					if ((v1 == null ? "" : v1.Name) != (v2 == null ? "" : v2.Name)) differs++;
				}
				Check(ref ok, same == 500 && differs > 150, "the same animal always looks the same (" + same + "/500); another world seed changes " + differs + "/500");
				// Parts switched off
				var noColours = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 9 };
				noColours.Disabled.Add(RandomizerSettings.Colours);
				WorldRandomizer.Current = noColours;
				int colouredOff = 0, alphasOn = 0;
				for (uint i = 1; i <= 1000; i++) { WorldRandomizer.Variant v = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Bear, i); if (v != null && !v.Alpha) colouredOff++; if (v != null && v.Alpha) alphasOn++; }
				var noAlphas = new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 9 };
				noAlphas.Disabled.Add(RandomizerSettings.Alphas);
				WorldRandomizer.Current = noAlphas;
				int alphasOff = 0;
				for (uint i = 1; i <= 1000; i++) { WorldRandomizer.Variant v = WorldRandomizer.VariantOf(AI_NetworkBehaviourType.Bear, i); if (v != null && v.Alpha) alphasOff++; }
				Check(ref ok, colouredOff == 0 && alphasOn > 50 && alphasOff == 0, "colours off: no colours (" + colouredOff + "), alphas still (" + alphasOn + "); alphas off: none (" + alphasOff + ")");
			}
			catch (Exception e) { Fail("randomizer rules: " + e); ok = false; }
			finally { WorldRandomizer.Current = keep; }
			if (ok) Log("PASS: randomizer rules"); else Fail("randomizer rules");
		}

		[ConsoleCommand(name: "CIOddities", docs: "Dev, anywhere: every oddity island and the boss lair can be made: set piece, loot, note, title; lair with a much tougher named beast, guards, hoard and quest; every object exists in this Raft")]
		public static void OdditiesCommand() { DynamicIslands.instance.StartCoroutine(OdditiesRoutine()); }

		static IEnumerator OdditiesRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			var expected = new Dictionary<string, string>
			{
				{ "van", "^Van" }, { "caravan", "^Caravan_" }, { "planecrash", "^Airplane$" }, { "boatwreck", "^BoatStranded$" }, { "shack", "^Balboa_Shack$" },
				{ "statue", "^(TangaroaFounderStatue|RaftMonument)$" }, { "rocket", "^CaravanRocket$" }, { "hut", "^Block_Foundation$" },
			};
			var files = new List<IslandFile>();
			var names = new HashSet<string>();
			foreach (var kv in expected.Concat(new[] { new KeyValuePair<string, string>("lair", "^Creature_") }))
			{
				MapType type = MapTypes.Get(kv.Key);
				if (type == null) { Fail("no map type '" + kv.Key + "'"); ok = false; continue; }
				float el;
				IslandGenSettings s = MapTypes.Roll(type, new System.Random(kv.Key.GetHashCode() & 0xFFFF), out el);
				IslandFile f = null;
				try { f = MapTypes.Create(type, s, el, MapTypes.FileName(type, s)); }
				catch (Exception e) { Fail(kv.Key + ": " + e); ok = false; continue; }
				files.Add(f);
				foreach (IslandObject o in f.Objects) names.Add(o.Name);
				int loot = f.Objects.Count(o => ContentCatalog.IsLootObject(o.Name));
				int notes = f.Objects.Count(o => ContentCatalog.IsNoteObject(o.Name));
				IslandObject piece = f.Objects.FirstOrDefault(o => System.Text.RegularExpressions.Regex.IsMatch(o.Name, kv.Value));
				string title = ObjectProps.Get(f.Props, IslandProps.Title);
				bool good = piece != null && loot > 0 && title.Length > 0 && (kv.Key == "lair" || notes > 0);
				if (kv.Key == "lair")
				{
					IslandObject beast = f.Objects.FirstOrDefault(o => o.Name.StartsWith("Creature_") && ObjectProps.GetFloat(o.Props, ObjectProps.CreatureHealth, 1f) >= 6f);
					good &= beast != null && f.Objects.Count(o => o.Name.StartsWith("Creature_")) >= 3 && f.Props.ContainsKey(IslandQuest.KeyTitle) && f.Objects.Any(o => ObjectProps.Get(o.Props, ObjectProps.NoteTitle) == "Hoard");
					Log("  lair: '" + title + "', beast " + (beast != null ? beast.Name + " x" + ObjectProps.GetFloat(beast.Props, ObjectProps.CreatureHealth, 1f) + " health, size " + ObjectProps.GetFloat(beast.Props, ObjectProps.CreatureSize, 1f) : "none") +
						", " + f.Objects.Count(o => o.Name.StartsWith("Creature_")) + " creature spots, quest '" + ObjectProps.Get(f.Props, IslandQuest.KeyTitle) + "'");
				}
				// Set pieces stand on the ground: their bottom within a metre or so of it (sunk a little, never floating)
				Check(ref ok, good, kv.Key + ": '" + title + "', " + f.Objects.Count + " objects, " + (piece != null ? piece.Name : "NO set piece") + ", " + loot + " loot, " + notes + " note(s)");
			}
			// The generic "oddity" type makes all kinds
			var kinds = new HashSet<string>();
			for (int i = 0; i < 40; i++)
			{
				MapType t = MapTypes.Get("oddity");
				float el;
				IslandGenSettings s = MapTypes.Roll(t, new System.Random(1000 + i), out el);
				IslandFile f = MapTypes.Create(t, s, el, "test");
				kinds.Add(ObjectProps.Get(f.Props, IslandProps.Title));
			}
			Check(ref ok, kinds.Count >= 6, "'oddity' makes " + kinds.Count + " kinds in 40 islands: " + string.Join(", ", kinds.ToArray()));
			// Every object exists in this Raft (the set pieces come from Raft's other islands)
			yield return PlaceableCatalog.EnsureLoaded(names.ToList());
			List<string> missing = names.Where(n => PlaceableCatalog.Get(n) == null).ToList();
			Check(ref ok, missing.Count == 0, names.Count + " kinds of objects, missing: " + (missing.Count == 0 ? "none" : string.Join(", ", missing.ToArray())));
			if (ok) Log("PASS: oddities and lairs"); else Fail("oddities and lairs");
		}

		#endregion

		#region In a world (host)

		[ConsoleCommand(name: "CIRandomizerWorld", docs: "Dev, world (host): turns the randomizer on (wild, a fixed seed) and checks Raft's islands: crates and clams moved (the same again), extras laid over them (animals, loot, finds) that load and spawn animals, coloured animals, an alpha's spoils, an oddity and a boss lair. CIRandomizerWorld [keep] [shots]")]
		public static void RandomizerWorldCommand(string[] args)
		{
			bool keep = args != null && args.Any(a => a.Equals("keep", StringComparison.OrdinalIgnoreCase));
			bool shots = args != null && args.Any(a => a.Equals("shots", StringComparison.OrdinalIgnoreCase));
			DynamicIslands.instance.StartCoroutine(RandomizerWorldRoutine(keep, shots));
		}

		static IEnumerator RandomizerWorldRoutine(bool keep, bool shots)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			RandomizerSettings before = WorldRandomizer.Current.Copy();
			int islandsBefore = IslandWorldState.Islands.Count;
			WorldRandomizer.ForgetSeen();
			WorldRandomizer.Set(new RandomizerSettings { Level = RandomizerSettings.Wild, Seed = 20260926 });
			Log("Randomizer: " + WorldRandomizer.Current.Describe());
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;

			// Raft's islands get handled (a second after they are seen)
			yield return new WaitForSeconds(4f);
			List<Landmark> spawned = WorldManager.AllLandmarks.Where(l => l != null && l.isSpawned).ToList();
			List<Landmark> nat = spawned.Where(WorldRandomizer.IsNatural).ToList();
			Check(ref ok, nat.Count > 0, spawned.Count + " of Raft's islands spawned, " + nat.Count + " plain ones randomized: " + string.Join(", ", nat.Select(l => l.name).Take(8).ToArray()));

			// Crates and clams: some moved; Raft still finds each by its old place; the same spots again
			var moved = new List<KeyValuePair<LandmarkItem, Vector3>>();
			int lootItems = 0;
			bool findable = true;
			foreach (Landmark l in nat)
				foreach (LandmarkItem i in (l.landmarkItems ?? new LandmarkItem[0]).Where(WorldRandomizer.IsRaftLoot))
				{
					lootItems++;
					Vector3? o = WorldRandomizer.OriginalOf(l, i);
					if (o.HasValue && (o.Value - i.transform.position).magnitude > 1f) moved.Add(new KeyValuePair<LandmarkItem, Vector3>(i, i.transform.position));
					if (l.GetLandmarkItemByLocalPosition(i.localLandmarkPosition) != i) findable = false;
				}
			Check(ref ok, moved.Count > 0 && moved.Count <= lootItems, moved.Count + " of " + lootItems + " crates and clams lie somewhere else (wild: about 80%)");
			Check(ref ok, findable, "Raft still finds every crate and clam by the place it knows (picking up, saving and the network use it)");
			bool land = moved.All(m => { Vector3? o = WorldRandomizer.OriginalOf(m.Key.GetComponentInParent<Landmark>(), m.Key); return !o.HasValue || (o.Value.y < -0.5f) == (m.Value.y < -0.5f); });
			Check(ref ok, land, "crates on land stay on land, under-water ones under water");
			WorldRandomizer.Rehandle();
			yield return new WaitForSeconds(3f);
			float drift = moved.Count == 0 ? 0f : moved.Max(m => (m.Key.transform.position - m.Value).magnitude);
			Check(ref ok, drift < 0.05f, "handled again (as after a reload or on another player's machine): the same spots (max difference " + drift.ToString("F3") + " m)");

			// Extras over Raft's islands
			List<IslandWorldState.Entry> extras = IslandWorldState.Islands.Where(WorldRandomizer.IsExtras).ToList();
			for (float t = 0; t < 20f && extras.Count == 0; t += 1f) { yield return new WaitForSeconds(1f); extras = IslandWorldState.Islands.Where(WorldRandomizer.IsExtras).ToList(); }
			Check(ref ok, extras.Count > 0, extras.Count + " of Raft's islands got extras (" + WorldRandomizer.SeenCount + " looked at)");
			Check(ref ok, extras.All(e => WorldRandomizer.IslandAt(e.Position) != null), "each lies over one of Raft's islands, centred on it");
			// The near ones load (like any custom island within 800 m)
			Network_Player player = RAPI.GetLocalPlayer();
			List<IslandWorldState.Entry> near = extras.Where(e => Flat(e.Position - raft).magnitude < CustomIslandSpawner.UnloadDistance - 200f).ToList();
			for (float t = 0; t < 30f && near.Any(e => e.Root == null && !e.Failed); t += 1f) yield return new WaitForSeconds(1f);
			Check(ref ok, near.All(e => e.Root != null), near.Count(e => e.Root != null) + " of " + near.Count + " near ones loaded");
			// Their animals come (on Raft's small islands a NavMesh is built for them first)
			List<CreatureSpawnPoint> spots = near.Where(e => e.Root != null).SelectMany(e => e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true)).ToList();
			for (float t = 0; t < 90f && spots.Any(p => p != null && p.RecordedAlive < 0); t += 1f) yield return new WaitForSeconds(1f);
			int alive = spots.Where(p => p != null).Sum(p => Math.Max(0, p.RecordedAlive));
			Check(ref ok, spots.Count == 0 || alive > 0, spots.Count + " extra animal spot(s) on near islands, " + alive + " animal(s) spawned: " +
				string.Join(", ", spots.Where(p => p != null).Select(p => p.Kind.Label + " " + p.RecordedAlive).ToArray()));
			int loot = near.Where(e => e.Root != null).Sum(e => e.Root.GetComponentsInChildren<LootCrate>(true).Length);
			Log("  loot containers on near extras: " + loot + "; all extras: " + string.Join("; ", extras.Select(e => e.HostName + (e.Root != null ? " (loaded)" : "")).ToArray()));

			// Animals: some have other colours (on their body's materials)
			yield return new WaitForSeconds(1.5f);
			int coloured = 0, checkedTint = 0;
			foreach (AI_NetworkBehaviour ai in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
			{
				string v;
				if (!WorldRandomizer.VariantOfIndex.TryGetValue(ai.ObjectIndex, out v)) continue;
				coloured++;
				Renderer r = WorldRandomizer.BodyOf(ai).FirstOrDefault();
				if (r == null) continue;
				var block = new MaterialPropertyBlock();
				r.GetPropertyBlock(block);
				Material m = r.sharedMaterial;
				if (m != null && m.HasProperty("_Color") && block.GetColor("_Color") != m.GetColor("_Color")) checkedTint++;
				else if (m != null && m.HasProperty("_Diffuse") && block.GetTexture("_Diffuse") is RenderTexture) checkedTint++; // (the shark: a coloured copy of its texture)
				if (coloured <= 6) Log("  " + ai.behaviourType + " #" + ai.ObjectIndex + ": " + v);
			}
			Check(ref ok, coloured > 0 && checkedTint > 0, coloured + " animal(s) with another look, " + checkedTint + " checked on their material");

			// An alpha's spoils: Raft's own dropped items, for every player
			Vector3 dropAt = player.transform.position + player.transform.forward * 2f;
			List<string> dropped = WorldRandomizer.DropSpoils(AI_NetworkBehaviourType.Boar, dropAt, 99);
			yield return new WaitForSeconds(1f);
			int pickups = UnityEngine.Object.FindObjectsOfType<PickupItem>().Count(p => p.isDropped && (p.transform.position - dropAt).magnitude < 6f);
			Check(ref ok, dropped.Count >= 3 && pickups >= dropped.Count, "an alpha warthog's spoils: " + string.Join(", ", dropped.ToArray()) + " (" + pickups + " dropped item(s) near)");
			foreach (PickupItem p in UnityEngine.Object.FindObjectsOfType<PickupItem>().Where(p => p.isDropped && (p.transform.position - dropAt).magnitude < 6f))
				PickupObjectManager.RemovePickupItemNetwork(p.GetComponent<PickupItem_Networked>());

			// An oddity island and a boss lair appear as the randomizer brings them
			var brought = new List<IslandWorldState.Entry>();
			foreach (string type in new[] { "oddity", "lair" })
			{
				int n = IslandWorldState.Islands.Count;
				string r = CustomIslandSpawner.TrySpawn(raft, true, CustomIslandSpawner.TypePrefix + type);
				IslandWorldState.Entry e = IslandWorldState.Islands.Count > n ? IslandWorldState.Islands[IslandWorldState.Islands.Count - 1] : null;
				// (Raft's sea is crowded: ahead of the raft may be full; the randomizer then tries again later. Here: the nearest free spot)
				if (e == null) { e = SpawnTypeNear(type, raft); r += "; placed in a free spot nearby instead"; }
				for (float t = 0; e != null && t < 60f && e.Root == null && !e.Failed; t += 1f) yield return new WaitForSeconds(1f);
				Check(ref ok, e != null && e.Root != null, type + ": " + r + (e != null && e.Root != null ? " - loaded, '" + (e.Root.GetComponent<IslandInfoTag>() != null ? e.Root.GetComponent<IslandInfoTag>().Title : "") + "', " + e.Root.GetComponentsInChildren<IslandObjectRef>(true).Length + " objects" : ""));
				if (e != null) brought.Add(e);
				// (the next one needs the room: custom islands keep 800 m apart)
				if (shots && e != null && e.Root != null) yield return RandomizerShots(new List<IslandWorldState.Entry>(), new List<IslandWorldState.Entry> { e });
				if (e != null) IslandWorldState.RemoveIds(new[] { e.Id }, true);
			}
			if (shots) yield return RandomizerShots(near, new List<IslandWorldState.Entry>());

			// Sailing far enough makes an oddity island due: it comes as soon as there is room ahead
			int count = IslandWorldState.Islands.Count;
			WorldRandomizer.OnSailed(20000f, CustomIslandSpawner.RaftPosition ?? raft);
			Check(ref ok, WorldRandomizer.OddityDue || IslandWorldState.Islands.Count > count, "after 20 km of sailing an oddity island is " + (IslandWorldState.Islands.Count > count ? "on its way: " + IslandWorldState.Islands[IslandWorldState.Islands.Count - 1].HostName : "due (no room ahead yet: tried again every 200 m)"));

			if (!keep)
			{
				IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(islandsBefore).Select(e => e.Id).ToList(), true);
				WorldRandomizer.Set(before);
				WorldRandomizer.ForgetSeen();
				Log("Removed the test's islands; randomizer back to " + WorldRandomizer.Current.Describe());
			}
			if (ok) Log("PASS: randomizer in a world"); else Fail("randomizer in a world");
		}

		/// <summary>A new island of a map type in the nearest free spot (the test's stand-in for sailing on until there is room).</summary>
		static IslandWorldState.Entry SpawnTypeNear(string typeName, Vector3 raft)
		{
			MapType type = MapTypes.Get(typeName);
			float el;
			IslandGenSettings s = MapTypes.Roll(type, new System.Random(), out el);
			string name = MapTypes.FileName(type, s);
			float radius = MapTypes.EstimatedRadius(s);
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raft, radius, 780f);
			if (!spot.HasValue) return null;
			CustomIslandSpawner.CacheSize(name, radius, el);
			IslandWorldState.Entry e = IslandWorldState.Add(name, spot.Value, null, false);
			e.Loading = true;
			DynamicIslands.instance.StartCoroutine(CustomIslandSpawner.GenerateAndSpawn(() => MapTypes.Create(type, s, el, name), name, e));
			return e;
		}

		/// <summary>Pictures: the extras on Raft's islands, the oddity island and the lair, and coloured animals.</summary>
		static IEnumerator RandomizerShots(List<IslandWorldState.Entry> extras, List<IslandWorldState.Entry> brought)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) yield break;
			SetHour(11f);
			int n = 0;
			foreach (IslandWorldState.Entry e in extras.Concat(brought).Where(x => x.Root != null))
			{
				// Some metres from the set piece (or a container), looking at it
				Transform target = e.Root.GetComponentsInChildren<IslandObjectRef>(true).Where(o => o.Props != null && o.Props.ContainsKey("set.piece")).Select(o => o.transform).FirstOrDefault() ??
					e.Root.GetComponentsInChildren<LootCrate>(true).Select(c => c.transform).FirstOrDefault() ?? e.Root.transform;
				Vector3 at = target.position + new Vector3(7f, 0f, 7f);
				RaycastHit hit;
				if (Physics.Raycast(at + Vector3.up * 60f, Vector3.down, out hit, 120f, ~0, QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 1.8f;
				at.y = Mathf.Max(at.y, target.position.y + 3f);
				yield return PutPlayer(player, at, at.y < 0.5f);
				Vector3 d = target.position - at;
				Look(player, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 25f);
				yield return new WaitForSeconds(2f);
				string name = "rnd_" + (n++) + "_" + e.HostName.Replace(" ", "_");
				ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(DynamicIslands.assetpath, "shot_" + name + ".png")));
				Log("Screenshot shot_" + name + ".png");
				yield return new WaitForSeconds(0.6f);
				if (n >= 6) break;
			}
			// A coloured animal
			AI_NetworkBehaviour ai = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().Where(a => WorldRandomizer.VariantOfIndex.ContainsKey(a.ObjectIndex) && a.behaviourType != AI_NetworkBehaviourType.Shark && a.transform.position.y > 0f)
				.OrderBy(a => (a.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
			if (ai != null)
			{
				Vector3 at = ai.transform.position + new Vector3(3f, 2f, 3f);
				yield return PutPlayer(player, at, false);
				Vector3 d = ai.transform.position - at;
				Look(player, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 20f);
				yield return new WaitForSeconds(1f);
				ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(DynamicIslands.assetpath, "shot_rnd_animal.png")));
				Log("Screenshot shot_rnd_animal.png: " + ai.behaviourType + " " + WorldRandomizer.VariantOfIndex[ai.ObjectIndex]);
				yield return new WaitForSeconds(0.6f);
			}
			// Back on the raft
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj != null) yield return PutPlayer(player, raftObj.transform.position + Vector3.up * 2f, false);
		}

		[ConsoleCommand(name: "CIRandomizerCheck", docs: "Dev, world: the randomizer's settings and extras as saved: CIRandomizerCheck <level> [min extras] (after CIRandomizerWorld keep, a save and a reload)")]
		public static void RandomizerCheckCommand(string[] args)
		{
			string level = args != null && args.Length > 0 ? args[0] : "wild";
			int min = args != null && args.Length > 1 ? int.Parse(args[1]) : 1;
			bool ok = true;
			Check(ref ok, WorldRandomizer.Current.LevelName.Equals(level, StringComparison.OrdinalIgnoreCase) && WorldRandomizer.Current.Seed != 0, "settings: " + WorldRandomizer.Current.Encode());
			int extras = IslandWorldState.Islands.Count(WorldRandomizer.IsExtras);
			Check(ref ok, extras >= min, extras + " extras in the world's list (at least " + min + ")");
			Log(WorldRandomizer.Describe().Replace("\n", " | "));
			if (ok) Log("PASS: randomizer check"); else Fail("randomizer check");
		}

		[ConsoleCommand(name: "CIRandomizerSig", docs: "Dev, world: what this machine worked out for the randomizer (settings, where Raft's crates and clams lie, how animals look, the extras): lines 'SIG ...' to compare between host and client")]
		public static void RandomizerSigCommand()
		{
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			Log("SIG set " + WorldRandomizer.Current.Encode());
			foreach (Landmark l in WorldManager.AllLandmarks.Where(x => x != null && x.isSpawned && WorldRandomizer.GroundOn(x) && WorldRandomizer.IsNatural(x)))
			{
				var spots = (l.landmarkItems ?? new LandmarkItem[0]).Where(WorldRandomizer.IsRaftLoot).Select(i => l.transform.InverseTransformPoint(i.transform.position))
					.Select(v => v.x.ToString("F1", inv) + "," + v.y.ToString("F1", inv) + "," + v.z.ToString("F1", inv)).ToArray();
				if (spots.Length > 0) Log("SIG loot " + WorldRandomizer.SpawnKey(l) + " " + string.Join(" ", spots));
			}
			foreach (AI_NetworkBehaviour ai in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().OrderBy(a => a.ObjectIndex))
			{
				if (CreatureSpawner.IsOnCustomIsland(ai)) continue;
				string v;
				Log("SIG animal " + ai.ObjectIndex + " " + ai.behaviourType + " " + (WorldRandomizer.VariantOfIndex.TryGetValue(ai.ObjectIndex, out v) ? v : "-"));
			}
			var extras = IslandWorldState.Islands.Where(WorldRandomizer.IsExtras).OrderBy(e => e.HostName).ToList();
			Log("SIG extras " + string.Join(" ", extras.Select(e => e.HostName).ToArray()));
			Log("SIG loaded " + extras.Count(e => e.Root != null) + " objects " + extras.Where(e => e.Root != null).Sum(e => e.Root.GetComponentsInChildren<IslandObjectRef>(true).Length) +
				" crates " + extras.Where(e => e.Root != null).Sum(e => e.Root.GetComponentsInChildren<LootCrate>(true).Length));
			Log("PASS: randomizer signature");
		}

		/// <summary>The moved crates and clams of Raft's spawned plain islands, with their island and place in its item list.</summary>
		static IEnumerable<KeyValuePair<string, LandmarkItem>> MovedLoot()
		{
			foreach (Landmark l in WorldManager.AllLandmarks.Where(x => x != null && x.isSpawned && WorldRandomizer.GroundOn(x) && WorldRandomizer.IsNatural(x) && x.landmarkItems != null))
				for (int i = 0; i < l.landmarkItems.Length; i++)
				{
					LandmarkItem it = l.landmarkItems[i];
					if (!WorldRandomizer.IsRaftLoot(it)) continue;
					Vector3? o = WorldRandomizer.OriginalOf(l, it);
					if (o.HasValue && (o.Value - it.transform.position).magnitude > 1f) yield return new KeyValuePair<string, LandmarkItem>(WorldRandomizer.SpawnKey(l) + "/" + i, it);
				}
		}

		[ConsoleCommand(name: "CIPickMoved", docs: "Dev, world (either player): picks up one of Raft's crates or clams the randomizer moved, as a player would; logs its key (island/item)")]
		public static void PickMovedCommand()
		{
			var m = MovedLoot().FirstOrDefault(x => x.Value.gameObject.activeInHierarchy && x.Value.GetComponentInChildren<PickupItem>() != null);
			if (m.Value == null) { Fail("no moved crate or clam to pick up"); return; }
			PickupItem item = m.Value.GetComponentInChildren<PickupItem>();
			Pickup pickup = RAPI.GetLocalPlayer().GetComponentInChildren<Pickup>(true);
			PutPlayerNear(item.transform);
			pickup.PickupItemByType(item, true);
			Log("Picked up the moved " + m.Value.name + " " + m.Key + " (" + (Raft_Network.IsHost ? "host" : "client") + ")");
			Log("PASS: picked moved " + m.Key);
		}

		[ConsoleCommand(name: "CIMovedState", docs: "Dev, world: whether one of Raft's moved crates or clams is still there: CIMovedState <island/item>")]
		public static void MovedStateCommand(string[] args)
		{
			string key = args != null && args.Length > 0 ? args[0] : "";
			var m = MovedLoot().FirstOrDefault(x => x.Key == key);
			if (m.Value == null)
			{
				// (picked up: Raft may have switched it off, then it is still in the list but not active)
				foreach (Landmark l in WorldManager.AllLandmarks.Where(x => x != null && x.isSpawned && x.landmarkItems != null))
				{
					string[] k = key.Split('/');
					int i;
					if (k.Length == 2 && WorldRandomizer.SpawnKey(l).ToString() == k[0] && int.TryParse(k[1], out i) && i < l.landmarkItems.Length) { m = new KeyValuePair<string, LandmarkItem>(key, l.landmarkItems[i]); break; }
				}
			}
			if (m.Value == null) { Fail("no item " + key); return; }
			PickupItem p = m.Value.GetComponentInChildren<PickupItem>(true);
			bool there = m.Value.gameObject.activeInHierarchy && (p == null || p.gameObject.activeInHierarchy);
			Log("MOVED " + key + " " + (there ? "there" : "gone") + " (" + m.Value.name + ")");
			Log("PASS: moved state");
		}

		[ConsoleCommand(name: "CIRandomizerClean", docs: "Dev, world (host): removes the randomizer's extras from this world and switches it off (after the persistence test)")]
		public static void RandomizerCleanCommand()
		{
			int n = IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(WorldRandomizer.IsExtras).Select(e => e.Id).ToList(), true);
			WorldRandomizer.Set(new RandomizerSettings());
			WorldRandomizer.ForgetSeen();
			Log("Removed " + n + " extras; randomizer off");
			Log("PASS: randomizer clean");
		}

		[ConsoleCommand(name: "CIRandomizerPending", docs: "Dev, main menu: the randomizer settings the next new world gets (as if chosen in the New Game box): CIRandomizerPending <off|light|normal|wild> [-part ...]")]
		public static void RandomizerPendingCommand(string[] args)
		{
			var s = new RandomizerSettings();
			foreach (string a in args ?? new string[0])
			{
				int l = Array.FindIndex(RandomizerSettings.LevelNames, n => n.Equals(a, StringComparison.OrdinalIgnoreCase));
				if (l >= 0) s.Level = l;
				else if (a.StartsWith("-")) s.Disabled.Add(a.Substring(1).ToLowerInvariant());
			}
			WorldRandomizer.Pending = s;
			Log("The next new world's randomizer: " + s.Describe());
			Log("PASS: randomizer pending");
		}

		#endregion

		[ConsoleCommand(name: "CIProbeGround", docs: "Dev, world: what the randomizer's ground finder sees on Raft's spawned plain islands (land, heights, slopes), and the shark's shader properties")]
		public static void ProbeGroundCommand()
		{
			foreach (Landmark l in WorldManager.AllLandmarks.Where(x => x != null && x.isSpawned && WorldRandomizer.IsNatural(x)))
			{
				var g = new LandGround(l);
				int hits = 0, dry = 0, beach = 0, under = 0;
				float top = float.MinValue;
				var owners = new Dictionary<string, int>();
				for (float x = -g.Radius; x <= g.Radius; x += 8f)
					for (float z = -g.Radius; z <= g.Radius; z += 8f)
					{
						Vector3 p, n;
						if (!g.Hit(g.Centre.x + x, g.Centre.z + z, out p, out n)) continue;
						hits++;
						float slope = Vector3.Angle(n, Vector3.up);
						if (p.y > 1.2f && slope < 26f) dry++;
						if (p.y > 0.25f && p.y < 2.2f) beach++;
						if (p.y < -3f && p.y > -14f) under++;
						top = Mathf.Max(top, p.y);
					}
				foreach (Collider c in l.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger && c.GetComponentInParent<LandmarkItem>() == null))
				{
					string k = c.GetType().Name + " L" + c.gameObject.layer;
					int v; owners.TryGetValue(k, out v); owners[k] = v + 1;
				}
				Log("ground '" + l.name + "' at " + l.transform.position.ToString("F0") + ": centre " + g.Centre.ToString("F0") + " radius " + g.Radius.ToString("F0") + ", " + hits + " hits, top " + top.ToString("F1") +
					", dry " + dry + ", beach " + beach + ", under water " + under + "; colliders " + string.Join(", ", owners.Select(kv => kv.Key + " x" + kv.Value).ToArray()));
				// What a ray straight down at the middle hits
				RaycastHit[] all = Physics.RaycastAll(new Vector3(l.transform.position.x, 250f, l.transform.position.z), Vector3.down, 500f, ~0, QueryTriggerInteraction.Ignore);
				Log("  down the middle: " + string.Join("; ", all.OrderBy(h => h.distance).Take(5).Select(h => h.collider.name + " (" + h.collider.GetType().Name + ", child " + h.collider.transform.IsChildOf(l.transform) + ") y " + h.point.y.ToString("F1")).ToArray()));
			}
			foreach (AI_NetworkBehaviour ai in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().Where(a => a.behaviourType == AI_NetworkBehaviourType.Shark))
				foreach (Renderer r in WorldRandomizer.BodyOf(ai))
					foreach (Material m in r.sharedMaterials.Where(m => m != null))
					{
						var props = new List<string>();
						for (int i = 0; i < m.shader.GetPropertyCount(); i++) props.Add(m.shader.GetPropertyName(i) + ":" + m.shader.GetPropertyType(i));
						Log("shark material " + m.name + " / " + m.shader.name + ": " + string.Join(", ", props.ToArray()));
					}
			Log("PASS: probed ground");
		}

		[ConsoleCommand(name: "CINewGameBoxShot", docs: "Dev, main menu: opens Raft's New Game box with the plan and randomizer panels, checks they fit inside it, takes shot_newgame.png and closes it")]
		public static void NewGameBoxShotCommand() { DynamicIslands.instance.StartCoroutine(NewGameBoxShot()); }

		static IEnumerator NewGameBoxShot()
		{
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("no New Game box (main menu?)"); yield break; }
			RandomizerSettings pending = WorldRandomizer.Pending;
			var shown = new RandomizerSettings { Level = RandomizerSettings.Normal };
			shown.Disabled.Add(RandomizerSettings.Alphas);
			WorldRandomizer.Pending = shown;
			box.gameObject.SetActive(true);
			box.Open();
			yield return new WaitForSeconds(1f);
			bool ok = true;
			RectTransform boxRect = (RectTransform)box.transform;
			foreach (string n in new[] { "CustomIslands_Plan", "CustomIslands_Randomizer" })
			{
				RectTransform r = box.transform.Find(n) as RectTransform;
				Vector3[] c = new Vector3[4], b = new Vector3[4];
				if (r != null) { r.GetWorldCorners(c); boxRect.GetWorldCorners(b); }
				bool inside = r != null && c[0].x >= b[0].x - 1f && c[0].y >= b[0].y - 1f && c[2].x <= b[2].x + 1f && c[2].y <= b[2].y + 1f;
				Check(ref ok, r != null && r.gameObject.activeInHierarchy && inside, n + (r == null ? " missing" : inside ? " inside the box" : " sticks out of the box"));
			}
			int buttons = box.transform.Find("CustomIslands_Randomizer") != null ? box.transform.Find("CustomIslands_Randomizer").GetComponentsInChildren<UnityEngine.UI.Button>(true).Length : 0;
			Check(ref ok, buttons == 1 + RandomizerSettings.Features.Length, buttons + " randomizer buttons (level + " + RandomizerSettings.Features.Length + " parts)");
			string file = System.IO.Path.GetFullPath(System.IO.Path.Combine(DynamicIslands.assetpath, "shot_newgame.png"));
			ScreenCapture.CaptureScreenshot(file);
			Log("Screenshot " + file);
			yield return new WaitForSeconds(1f);
			box.gameObject.SetActive(false);
			WorldRandomizer.Pending = pending;
			if (ok) Log("PASS: New Game box"); else Fail("New Game box");
		}

		[ConsoleCommand(name: "CITime", docs: "Dev, world: sets the time of day (hour 0-24), as Raft's own cheat does: CITime <hour>")]
		public static void TimeCommand(string[] args) { SetHour(args != null && args.Length > 0 ? float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 12f); Log("PASS: time set"); }

		static void SetHour(float hour)
		{
			UnityEngine.AzureSky.AzureSkyController sky = ComponentManager<UnityEngine.AzureSky.AzureSkyController>.Value;
			if (sky != null && sky.timeOfDay != null) sky.timeOfDay.hour = hour;
		}

		[ConsoleCommand(name: "CIProbeCollider", docs: "Dev, world: why rays do or don't hit Raft's spawned islands: their terrain colliders, scenes and physics scenes")]
		public static void ProbeColliderCommand()
		{
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			Log("time scale " + Time.timeScale + ", auto sync " + Physics.autoSyncTransforms + ", auto simulation " + Physics.autoSimulation + ", raft at " + raft.ToString("F0"));
			foreach (Landmark l in WorldManager.AllLandmarks.Where(x => x != null && x.isSpawned))
			{
				foreach (TerrainCollider tc in l.GetComponentsInChildren<TerrainCollider>(true))
				{
					Vector3 c = tc.bounds.center;
					RaycastHit hit;
					bool own = tc.Raycast(new Ray(new Vector3(c.x, 300f, c.z), Vector3.down), out hit, 600f);
					bool world = Physics.Raycast(new Vector3(c.x, 300f, c.z), Vector3.down, out hit, 600f);
					bool scene = tc.gameObject.scene.GetPhysicsScene() == Physics.defaultPhysicsScene;
					Log(l.name + " at " + (l.transform.position - raft).magnitude.ToString("F0") + " m: terrain '" + tc.name + "' enabled " + tc.enabled + " active " + tc.gameObject.activeInHierarchy + " scene '" + tc.gameObject.scene.name +
						"' default physics " + scene + ", bounds " + tc.bounds.center.ToString("F0") + " size " + tc.bounds.size.ToString("F0") + ", terrain at " + tc.transform.position.ToString("F0") +
						", own ray " + own + ", world ray " + world + (world ? " (" + hit.collider.name + ")" : "") + ", data " + (tc.terrainData != null ? tc.terrainData.size.ToString("F0") : "none"));
				}
			}
			Log("PASS: probed colliders");
		}

		#region Like Raft's islands, large islands, caves

		static readonly System.Text.RegularExpressions.Regex Cuttable = new System.Text.RegularExpressions.Regex(@"^Pickup_Landmark_(Tree_\w+|MangoTree|Palmtree \d+)$");

		/// <summary>Raft's plain tropical islands from raft_land.txt: land (m²), land objects, trees to cut, harvestables on land.</summary>
		static List<float[]> RaftIslandNumbers()
		{
			var land = new Dictionary<string, float>();
			var counts = new Dictionary<string, float[]>();
			var seen = new HashSet<string>();
			byte[] bytes = RaftIslands.ModFile(RaftLand.FileName);
			if (bytes == null) return new List<float[]>();
			foreach (string raw in System.Text.Encoding.UTF8.GetString(bytes).Split('\n'))
			{
				string[] f = raw.TrimEnd('\r').Split('\t');
				if (f.Length < 5 || !System.Text.RegularExpressions.Regex.IsMatch(f[1], @"^(Big|Big island .+|Small island \d+)$")) continue;
				if (f[0] == "land") land[f[1]] = f[4].Split(' ').Where(x => x.Contains(':')).Sum(x => float.Parse(x.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture));
				else if (f[0] == "lobj" && seen.Add(f[1] + "|" + f[2]))
				{
					float[] c;
					if (!counts.TryGetValue(f[1], out c)) counts[f[1]] = c = new float[3];
					float n = float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture);
					c[0] += n;
					if (Cuttable.IsMatch(f[2])) c[1] += n;
					if (f[2].StartsWith("Pickup_Landmark_")) c[2] += n;
				}
			}
			return land.Where(kv => counts.ContainsKey(kv.Key)).Select(kv => new[] { kv.Value, counts[kv.Key][0], counts[kv.Key][1], counts[kv.Key][2] }).ToList();
		}

		/// <summary>A generated island file's land (m²), land objects (nature, not content), trees to cut, harvestables on land, flowers.</summary>
		static float[] IslandNumbers(IslandFile f)
		{
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1), sea = f.WaterLevel;
			int cells = 0;
			for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) if (f.Heights[z, x] * f.TerrainSize.y > sea + 0.1f) cells++;
			var nature = f.Objects.Where(o => o.Position.y > sea + 0.2f && (o.Props == null || o.Props.Count == 0)).ToList();
			return new float[] { cells * step * step, nature.Count, nature.Count(o => Cuttable.IsMatch(o.Name)), nature.Count(o => o.Name.StartsWith("Pickup_Landmark_")), nature.Count(o => o.Name.Contains("Flower")) };
		}

		[ConsoleCommand(name: "CIIslandsLikeRaft", docs: "Dev, anywhere: the randomizer's islands fit in Raft's world: land objects per 1000 m² and trees to cut compared with Raft's own small and big islands (raft_land.txt); large islands have puffer fish, warthogs, animals to catch, scenes and a cave")]
		public static void IslandsLikeRaftCommand() { DynamicIslands.instance.StartCoroutine(IslandsLikeRaftRoutine()); }

		static IEnumerator IslandsLikeRaftRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			List<float[]> raft = RaftIslandNumbers();
			List<float[]> small = raft.Where(x => x[0] < 5000f).ToList(), big = raft.Where(x => x[0] >= 5000f).ToList();
			Func<List<float[]>, int, float, float> per = (list, i, scale) => list.Count == 0 ? 0f : list.Average(x => x[i] / x[0] * scale);
			Log("Raft's small islands: " + small.Count + ", " + (small.Count > 0 ? small.Min(x => x[0]).ToString("F0", inv) + "-" + small.Max(x => x[0]).ToString("F0", inv) : "?") + " m² of land, " +
				(small.Count > 0 ? small.Min(x => x[1] / x[0] * 1000f).ToString("F0", inv) + "-" + small.Max(x => x[1] / x[0] * 1000f).ToString("F0", inv) : "?") + " land objects per 1000 m², " +
				(small.Count > 0 ? small.Min(x => x[2]).ToString("F0", inv) + "-" + small.Max(x => x[2]).ToString("F0", inv) : "?") + " trees to cut");
			Log("Raft's big islands: " + big.Count + ", " + (big.Count > 0 ? big.Min(x => x[1] / x[0] * 1000f).ToString("F0", inv) + "-" + big.Max(x => x[1] / x[0] * 1000f).ToString("F0", inv) : "?") + " land objects per 1000 m², " +
				(big.Count > 0 ? big.Min(x => x[2] / x[0] * 1000f).ToString("F2", inv) + "-" + big.Max(x => x[2] / x[0] * 1000f).ToString("F2", inv) : "?") + " trees to cut per 1000 m²");
			if (small.Count == 0 || big.Count == 0) { Fail("raft_land.txt has no plain islands"); yield break; }
			float smallMin = small.Min(x => x[1] / x[0] * 1000f), smallMax = small.Max(x => x[1] / x[0] * 1000f);
			float bigMin = big.Min(x => x[1] / x[0] * 1000f), bigMax = big.Max(x => x[1] / x[0] * 1000f);
			float cutMin = big.Min(x => x[2] / x[0] * 1000f), cutMax = big.Max(x => x[2] / x[0] * 1000f);

			var types = RandomizerContent.Oddities.Select(o => o[0]).Concat(new[] { "lair", "large", "large", "large", "large" }).ToList();
			int flowers = 0, tropical = 0;
			for (int i = 0; i < types.Count; i++)
			{
				MapType type = MapTypes.Get(types[i]);
				float el;
				IslandGenSettings s = MapTypes.Roll(type, new System.Random(4200 + i), out el);
				IslandFile f = null;
				try { f = MapTypes.Create(type, s, el, "likeraft"); }
				catch (Exception e) { Fail(types[i] + ": " + e); ok = false; continue; }
				float[] n = IslandNumbers(f);
				float dens = n[1] / n[0] * 1000f, cut = n[2];
				bool isBig = n[0] >= 5000f;
				// Small islands: objects as dense as on Raft's small islands (loosely), a few trees to cut; big: as Raft's big ones
				bool good = isBig ? dens >= bigMin * 0.6f && dens <= bigMax * 1.6f && cut / n[0] * 1000f >= cutMin * 0.5f && cut / n[0] * 1000f <= cutMax * 2f
					: dens >= smallMin * 0.4f && dens <= smallMax * 1.5f && (s.Style != TerrainPainter.Tropical || (cut >= 1 && cut <= 14));
				if (s.Style == TerrainPainter.Tropical) { tropical++; if (n[4] > 0) flowers++; }
				string extra = "";
				if (types[i] == "large")
				{
					int puffer = f.Objects.Count(o => o.Name == "Creature_PufferFish"), boars = f.Objects.Count(o => o.Name == "Creature_Boar" || o.Name.StartsWith("Creature_") && o.Position.y > f.WaterLevel && ContentCatalog.CreatureOf(o.Name) != null && ContentCatalog.CreatureOf(o.Name).Category == ContentCatalog.HostileCategory);
					int tame = f.Objects.Count(o => ContentCatalog.CreatureOf(o.Name) != null && ContentCatalog.CreatureOf(o.Name).Category == ContentCatalog.CatchableCategory);
					int scene = f.Objects.Count(o => o.Props != null && o.Props.ContainsKey("set.piece") && !o.Props.ContainsKey("cave"));
					int cave = f.Objects.Count(o => o.Props != null && o.Props.ContainsKey("cave"));
					int loot = f.Objects.Count(o => ContentCatalog.IsLootObject(o.Name));
					extra = "; '" + ObjectProps.Get(f.Props, IslandProps.Title) + "' " + TerrainPainter.StyleName(s.Style) + ", " + puffer + " puffer fish, " + boars + " hostile spots, " + tame + " to catch, " + scene + " scene props, " + cave + " cave pieces, " + loot + " loot";
					good &= puffer >= 3 && boars >= 2 && tame >= 2 && loot >= 4 && scene >= 3 && (cave >= 1 || !RandomizerIslands.CanBuildCaves);
				}
				Check(ref ok, good, types[i] + ": " + n[0].ToString("F0", inv) + " m² of land, " + n[1].ToString("F0", inv) + " land objects (" + dens.ToString("F0", inv) + " per 1000 m²), " + cut.ToString("F0", inv) + " trees to cut, " +
					n[3].ToString("F0", inv) + " harvestables, " + n[4].ToString("F0", inv) + " flowers" + extra);
				yield return null;
			}
			// Every prop the scenes use is measured, stands on the ground (nothing that hangs from its pivot), and each theme has a centrepiece
			var themeProps = RandomizerIslands.Themes.SelectMany(t => t.Anchors.Concat(t.Medium).Concat(t.Small)).Distinct().ToList();
			var unmeasured = themeProps.Where(n => !RaftProps.Has(n)).ToList();
			var hanging = themeProps.Where(n => RaftProps.Has(n) && !RaftProps.Standable(n)).ToList();
			var bare = RandomizerIslands.Themes.Where(t => !t.Anchors.Any(RaftProps.Standable)).Select(t => t.Name).ToList();
			Check(ref ok, unmeasured.Count == 0 && hanging.Count == 0 && bare.Count == 0, themeProps.Count + " scene props in " + RandomizerIslands.Themes.Length + " themes; not measured: " + (unmeasured.Count == 0 ? "none" : string.Join(", ", unmeasured.ToArray())) +
				"; hanging: " + (hanging.Count == 0 ? "none" : string.Join(", ", hanging.ToArray())) + "; themes without a centrepiece: " + (bare.Count == 0 ? "none" : string.Join(", ", bare.ToArray())));
			Check(ref ok, RandomizerIslands.CanBuildCaves, "caves: " + string.Join(", ", RandomizerIslands.Dens.Where(n => RaftProps.Get(n) != null && RaftProps.Get(n).IsCave).ToArray()) + " measured as dens");
			Check(ref ok, flowers > 0 || !PlaceableCatalog.IsLoaded("Pickup_Landmark_Flower_Yellow"), "Raft's flowers grow on " + flowers + " of " + tropical + " tropical islands" + (PlaceableCatalog.IsLoaded("Pickup_Landmark_Flower_Yellow") ? "" : " (this Raft's catalog has none)"));
			if (ok) Log("PASS: islands like Raft's"); else Fail("islands like Raft's");
		}

		[ConsoleCommand(name: "CIRandomizerLarge", docs: "Dev, world (host): a large island near the raft - loads, its animals (warthogs, animals to catch, puffer fish), scenes, and its cave: players walk in, the guard wakes inside it; pictures shot_large_*. CIRandomizerLarge [keep]")]
		public static void RandomizerLargeCommand(string[] args) { DynamicIslands.instance.StartCoroutine(RandomizerLargeRoutine(args != null && args.Any(a => a == "keep"))); }

		static IEnumerator RandomizerLargeRoutine(bool keep)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			SetHour(11f);
			IslandWorldState.Entry e = SpawnTypeNear("large", raft);
			if (e == null) { Fail("no free spot for a large island near the raft (sail on)"); yield break; }
			for (float t = 0; t < 90f && e.Root == null && !e.Failed; t += 1f) yield return new WaitForSeconds(1f);
			if (e.Root == null) { Fail("the large island didn't load"); yield break; }
			GameObject root = e.Root;
			var tag = root.GetComponent<IslandInfoTag>();
			var refs = root.GetComponentsInChildren<IslandObjectRef>(true).ToList();
			int scene = refs.Count(r => r.Props != null && r.Props.ContainsKey("set.piece") && !r.Props.ContainsKey("cave"));
			List<Transform> caves = root.GetComponentsInChildren<Transform>(true).Where(t => RaftProps.Get(t.name) != null && RaftProps.Get(t.name).IsCave).ToList();
			Check(ref ok, tag != null && scene >= 3, "'" + (tag != null ? tag.Title : "?") + "' loaded: " + refs.Count + " objects with settings, " + scene + " scene props, " + caves.Count + " cave pieces");
			// Its animals (not the cave's guard: it waits)
			List<CreatureSpawnPoint> spots = root.GetComponentsInChildren<CreatureSpawnPoint>(true).ToList();
			for (float t = 0; t < 120f && spots.Any(p => p.RecordedAlive == CreatureSpawnPoint.Spawning || (p.RecordedAlive == -1 && ObjectProps.Get(p.Props, ObjectProps.CreatureZone).Length == 0)); t += 1f) yield return new WaitForSeconds(1f);
			Check(ref ok, spots.Count(p => p.RecordedAlive > 0) >= 5, spots.Count + " creature spots, alive: " + string.Join(", ", spots.GroupBy(p => p.Kind.Label).Select(g => g.Key + " " + g.Sum(p => Math.Max(0, p.RecordedAlive))).ToArray()));
			Network_Player player = RAPI.GetLocalPlayer();
			// Pictures: from above, of each scene
			Vector3 mid = root.transform.position + (tag != null ? tag.LocalCentre : Vector3.zero);
			yield return PutPlayer(player, mid + new Vector3(0f, 120f, -140f), false);
			Look(player, 0f, 35f);
			yield return new WaitForSeconds(2.5f);
			Shot("large_overview");
			foreach (IslandObjectRef r in refs.Where(x => x.Props != null && x.Props.ContainsKey("set.piece") && !x.Props.ContainsKey("cave") && RaftProps.Get(x.Name) != null && RandomizerIslands.Themes.Any(th => th.Anchors.Contains(x.Name))).Take(3))
				yield return ShotOf(player, r.transform, "large_scene_" + r.Name.Replace(" ", "_"), 9f);
			// The cave: the mouth, then inside; the guard wakes and stands in the passage
			if (caves.Count > 0)
			{
				TriggerZone zone = root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.Id == "cave");
				Transform first = caves[0];
				yield return ShotOf(player, first, "large_cave_mouth", 8f);
				if (zone != null)
				{
					yield return PutPlayer(player, zone.transform.position + Vector3.up * 1.0f, false);
					yield return new WaitForSeconds(4f);
					Shot("large_cave_inside");
					CreatureSpawnPoint guard = spots.FirstOrDefault(p => ObjectProps.Get(p.Props, ObjectProps.CreatureZone) == "cave");
					for (float t = 0; guard != null && t < 60f && guard.RecordedAlive <= 0; t += 1f) yield return new WaitForSeconds(1f);
					AI_NetworkBehaviour ai = guard != null ? guard.Spawned.FirstOrDefault(a => a != null) : null;
					float off = ai != null ? Mathf.Abs(ai.transform.position.y - zone.transform.position.y) : 99f;
					Check(ref ok, guard == null || (ai != null && off < 2.5f), guard == null ? "no guard in this cave" : ai == null ? "the cave's guard didn't come" : "the cave's " + guard.Kind.Label + " woke inside it (" + off.ToString("F1") + " m from the floor)");
					LootCrate chest = root.GetComponentsInChildren<LootCrate>(true).OrderBy(c => (c.transform.position - zone.transform.position).sqrMagnitude).FirstOrDefault();
					Check(ref ok, chest != null && (chest.transform.position - zone.transform.position).magnitude < 20f, "a hoard at the end of the cave");
				}
				else Check(ref ok, false, "the cave has no zone");
			}
			else Log("  (no cave: " + (RandomizerIslands.CanBuildCaves ? "no spot fit" : "no measured dens") + ")");
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj != null) yield return PutPlayer(player, raftObj.transform.position + Vector3.up * 2f, false);
			if (!keep) IslandWorldState.RemoveIds(new[] { e.Id }, true);
			if (ok) Log("PASS: a large island"); else Fail("a large island");
		}

		static void Shot(string name)
		{
			ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath(System.IO.Path.Combine(DynamicIslands.assetpath, "shot_" + name + ".png")));
			Log("Screenshot shot_" + name + ".png");
		}

		/// <summary>A picture of t from about distance metres away on the ground, looking at it.</summary>
		static IEnumerator ShotOf(Network_Player player, Transform t, string name, float distance)
		{
			Vector3 at = t.position + new Vector3(distance * 0.7f, 0f, distance * 0.7f);
			RaycastHit hit;
			if (Physics.Raycast(at + Vector3.up * 80f, Vector3.down, out hit, 160f, ~0, QueryTriggerInteraction.Ignore)) at.y = hit.point.y + 1.8f;
			at.y = Mathf.Max(at.y, t.position.y + 2f);
			yield return PutPlayer(player, at, at.y < 0.5f);
			Vector3 d = t.position - at;
			Look(player, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -30f, 45f));
			yield return new WaitForSeconds(2f);
			Shot(name);
			yield return new WaitForSeconds(0.6f);
		}

		[ConsoleCommand(name: "CIGrotto", docs: "Dev, world (host): the nearest of Raft's big islands gets a den (one of Balboa's cave outcrops) and an outpost (the randomizer's extras, forced): they load, the guard wakes inside the cave; pictures shot_grotto_*")]
		public static void GrottoCommand(string[] args) { DynamicIslands.instance.StartCoroutine(GrottoRoutine(args != null && args.Any(a => a == "keep"))); }

		static IEnumerator GrottoRoutine(bool keep)
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			Landmark big = WorldManager.AllLandmarks.Where(l => l != null && l.isSpawned && WorldRandomizer.KindOf(l) == "Landmark_Big").OrderBy(l => (l.transform.position - raft).sqrMagnitude).FirstOrDefault();
			if (big == null) { Fail("none of Raft's big islands is spawned near the raft (sail on)"); yield break; }
			RandomizerSettings before = WorldRandomizer.Current.Copy();
			if (!WorldRandomizer.Current.On) WorldRandomizer.Set(new RandomizerSettings { Level = RandomizerSettings.Normal, Seed = 77 });
			// Go there first: Raft switches the ground of far islands off
			yield return PutPlayer(player, big.transform.position + Vector3.up * 80f, false);
			for (float t = 0; t < 30f && !WorldRandomizer.GroundOn(big); t += 1f) yield return new WaitForSeconds(1f);
			SetHour(11f);
			RandomizerContent.ForceBigFinds = true;
			int n = IslandWorldState.Islands.Count;
			try { DynamicIslands.instance.StartCoroutine(WorldRandomizer.ForceExtras(big, 1234)); }
			finally { }
			yield return new WaitForSeconds(3f);
			RandomizerContent.ForceBigFinds = false;
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(x => WorldRandomizer.IsExtras(x));
			for (float t = 0; e != null && t < 60f && e.Root == null && !e.Failed; t += 1f) yield return new WaitForSeconds(1f);
			if (e == null || e.Root == null) { Fail("the extras didn't load"); yield break; }
			List<Transform> caves = e.Root.GetComponentsInChildren<Transform>(true).Where(t => RaftProps.Get(t.name) != null && RaftProps.Get(t.name).IsCave).ToList();
			int outpost = e.Root.GetComponentsInChildren<IslandObjectRef>(true).Count(r => r.Props != null && r.Props.ContainsKey("set.piece") && !r.Props.ContainsKey("cave") && RaftProps.Get(r.Name) != null && !RaftProps.Get(r.Name).Name.Contains("Boulder"));
			Check(ref ok, caves.Count >= 1 || !RandomizerIslands.CanBuildCaves, "'" + big.name + "': " + (caves.Count > 0 ? "a den (" + caves[0].name + ")" : "no den") + (RandomizerIslands.CanBuildCaves ? "" : " (no measured cave pieces)") + ", " + outpost + " outpost props");
			if (caves.Count > 0)
			{
				yield return ShotOf(player, caves[0], "grotto_mound", 12f);
				TriggerZone zone = e.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.Id == "cave");
				if (zone != null)
				{
					yield return PutPlayer(player, zone.transform.position + Vector3.up * 1.0f, false);
					yield return new WaitForSeconds(4f);
					Shot("grotto_inside");
					CreatureSpawnPoint guard = e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).FirstOrDefault(p => ObjectProps.Get(p.Props, ObjectProps.CreatureZone) == "cave");
					for (float t = 0; guard != null && t < 60f && guard.RecordedAlive <= 0; t += 1f) yield return new WaitForSeconds(1f);
					AI_NetworkBehaviour ai = guard != null ? guard.Spawned.FirstOrDefault(a => a != null) : null;
					float off = ai != null ? Mathf.Abs(ai.transform.position.y - zone.transform.position.y) : 99f;
					Check(ref ok, ai != null && off < 2.5f, guard == null ? "no guard" : ai == null ? "the guard didn't come" : "the " + guard.Kind.Label + " woke inside the cave (" + off.ToString("F1") + " m from the floor)");
				}
			}
			Transform scene = e.Root.GetComponentsInChildren<IslandObjectRef>(true).Where(r => r.Props != null && r.Props.ContainsKey("set.piece") && RandomizerIslands.Themes.Any(th => th.Anchors.Contains(r.Name))).Select(r => r.transform).FirstOrDefault();
			if (scene != null) yield return ShotOf(player, scene, "grotto_outpost", 9f);
			Raft raftObj = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raftObj != null) yield return PutPlayer(player, raftObj.transform.position + Vector3.up * 2f, false);
			if (!keep)
			{
				IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(n).Select(x => x.Id).ToList(), true);
				IslandWorldState.RemoveIds(new[] { e.Id }, true);
				WorldRandomizer.Set(before);
			}
			if (ok) Log("PASS: a cave on Raft's island"); else Fail("a cave on Raft's island");
		}

		[ConsoleCommand(name: "CIModeParity", docs: "Dev, anywhere: the mod spawns creatures in each of Raft's game modes exactly as Raft's own islands do (LandmarkEntitySpawner.ShouldEntityBeSpawnedInGamemode, every mode x every animal kind)")]
		public static void ModeParityCommand()
		{
			bool ok = true;
			var modes = new List<SO_GameModeValue>();
			try
			{
				var arr = typeof(GameModeValueManager).GetField("gameModeValues", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null) as SO_GameModeValue[];
				if (arr != null) modes.AddRange(arr.Where(m => m != null));
				foreach (string f in new[] { "creative", "peaceful", "normal", "hard" })
				{
					var m = typeof(GameModeValueManager).GetField(f, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null) as SO_GameModeValue;
					if (m != null && !modes.Contains(m)) modes.Add(m);
				}
			}
			catch (Exception e) { Fail("Raft's game modes: " + e.Message); return; }
			if (modes.Count == 0) { Fail("Raft's game modes aren't set up yet (main menu once?)"); return; }
			var go = new GameObject("CI_ModeParity");
			go.SetActive(false);
			var spawner = go.AddComponent<LandmarkEntitySpawner>();
			var raft = typeof(LandmarkEntitySpawner).GetMethod("ShouldEntityBeSpawnedInGamemode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
			int compared = 0, differ = 0;
			try
			{
				foreach (SO_GameModeValue m in modes)
				{
					var off = new List<string>();
					foreach (AI_NetworkBehaviourType t in Enum.GetValues(typeof(AI_NetworkBehaviourType)))
					{
						bool theirs = (bool)raft.Invoke(spawner, new object[] { m, t }), ours = CreatureSpawner.SpawnsInMode(t, m);
						compared++;
						if (theirs != ours) { differ++; Log("  " + m.gameMode + " " + t + ": Raft " + theirs + ", the mod " + ours); }
						if (!theirs) off.Add(t.ToString());
					}
					Log("  " + m.gameMode + ": Raft leaves out " + (off.Count == 0 ? "nothing" : string.Join(", ", off.ToArray())));
				}
			}
			finally { UnityEngine.Object.Destroy(go); }
			Check(ref ok, differ == 0 && compared > 0, compared + " mode x kind pairs compared with Raft's own rule, " + differ + " differ");
			if (ok) Log("PASS: creatures per game mode as Raft"); else Fail("creatures per game mode as Raft");
		}

		[ConsoleCommand(name: "CIStorySafe", docs: "Dev, main menu or editor: opens each of Raft's island scenes and checks the randomizer changes only its plain islands (big and small), never a story island, the stranded boat, the pilot's island or a floating raft")]
		public static void StorySafeCommand() { DynamicIslands.instance.StartCoroutine(StorySafeRoutine()); }

		static IEnumerator StorySafeRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			int plain = 0, story = 0;
			var wrong = new List<string>();
			foreach (string scene in PlaceableCatalog.LandmarkSceneNames())
			{
				string sceneName = scene;
				yield return PlaceableCatalog.VisitScene(sceneName, s => StorySafeScene(s, sceneName, wrong, n => { if (n) plain++; else story++; }));
				yield return null;
			}
			Check(ref ok, plain > 0 && story > 0 && wrong.Count == 0, plain + " plain island(s) randomized, " + story + " left alone; wrong: " + (wrong.Count == 0 ? "none" : string.Join("; ", wrong.ToArray())));
			if (ok) Log("PASS: Raft's story islands left alone"); else Fail("Raft's story islands left alone");
		}

		static IEnumerator StorySafeScene(UnityEngine.SceneManagement.Scene s, string sceneName, List<string> wrong, Action<bool> count)
		{
			foreach (GameObject root in s.GetRootGameObjects())
				foreach (Landmark l in root.GetComponentsInChildren<Landmark>(true))
				{
					// (a scene's island is named like the scene: "32#Landmark_Big#..." - that is what the randomizer reads)
					string saved = l.name;
					l.name = sceneName;
					bool natural = WorldRandomizer.IsNatural(l);
					l.name = saved;
					bool shouldBe = System.Text.RegularExpressions.Regex.IsMatch(sceneName, "#Landmark_(Big|Small)#") ||
						(System.Text.RegularExpressions.Regex.IsMatch(sceneName, "Small") && !System.Text.RegularExpressions.Regex.IsMatch(sceneName, "Radar|Vasagatan|Tangaroa|Varuna|Utopia|Pilot|Boat|#Landmark_Raft#"));
					Log("  " + sceneName + ": " + (natural ? "randomized" : "left alone"));
					count(natural);
					if (natural && !shouldBe) wrong.Add(sceneName + " would be randomized");
					if (!natural && System.Text.RegularExpressions.Regex.IsMatch(sceneName, "#Landmark_(Big|Small)#")) wrong.Add(sceneName + " (a plain island) would be left alone");
				}
			yield break;
		}

		[ConsoleCommand(name: "CICaveState", docs: "Dev, world (either player): a cave of an island as this machine has it: its guard (awake? animals of its kind in the den) and its hoard (opened?): CICaveState <island>")]
		public static void CaveStateCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			Transform den = e.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => RaftProps.Get(t.name) != null && RaftProps.Get(t.name).IsCave);
			if (den == null) { Fail("no cave on '" + e.HostName + "'"); return; }
			PropInfo p = RaftProps.Get(den.name);
			float reach = Mathf.Max(p.Size.x, p.Size.z) * 0.5f;
			Vector3 middle = den.position + den.rotation * p.Centre;
			CreatureSpawnPoint guard = e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).FirstOrDefault(s => ObjectProps.Get(s.Props, ObjectProps.CreatureZone) == "cave");
			int inside = guard == null ? 0 : UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().Count(a => a.behaviourType == guard.Kind.Type && new Vector2(a.transform.position.x - middle.x, a.transform.position.z - middle.z).magnitude < reach);
			TriggerZone zone = e.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.Id == "cave");
			LootCrate hoard = e.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault(c => { IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>(); return r != null && ObjectProps.Get(r.Props, ObjectProps.NoteTitle).EndsWith("hoard"); });
			Log("CAVE '" + e.HostName + "' " + den.name + ": zone " + (zone == null ? "none" : ContentState.IsUsed(e, zone.StateKey) ? "fired" : "waiting") + ", guard " + (guard == null ? "none" : guard.Kind.Label + " " + inside + " in the den") +
				(Raft_Network.IsHost && guard != null ? " (recorded alive " + guard.RecordedAlive + ")" : "") + ", hoard " + (hoard == null ? "none" : hoard.Looted ? "opened" : "closed"));
			Log("PASS: cave state");
		}

		#endregion

		/// <summary>Raft's objects the randomizer builds with: set pieces and props of the quest islands, and cave pieces.</summary>
		internal static readonly string[] MeasuredProps =
		{
			// Set pieces (oddity islands)
			"Van_1", "Van_2", "Van3", "Van_4", "Van_5", "Caravan_Blue_01", "Caravan_Green_01", "Caravan_Green_02", "Caravan_Yellow_01", "CaravanBarebones1", "Airplane", "BoatStranded",
			"Balboa_Shack", "Balboa_DecorationPrefabBase_SimpleTent", "CaravanRocket", "CaravanRocketDebris_Body1", "CaravanRocketDebris_Body2", "CaravanRocketDebris_Top1", "CaravanRocketDebris_Exhaust",
			"CaravanRocketDebris_Leg1", "CaravanRocketDebris_Door", "CaravanRocketDebris_Canister", "TangaroaFounderStatue", "RaftMonument", "Well", "Tire_02", "Tire_03", "Pallet", "RT_PlasticBoat",
			"WingBroken", "BackWingBroken", "PropellerBlade", "BoatLandmark_Flag",
			// Scrapyard (Varuna Point)
			"VP_Excavator", "VP_Forklift", "VP_Dumpster01", "VP_Dumpster02", "VP_ConcretePipe01", "VP_ConcretePipe02", "VP_BrickStack01", "VP_BrickStack02", "VP_BrickStack03",
			"VP_MetalCrate01", "VP_MetalCrate02", "VP_MetalCrate03", "VP_ExplosiveBarrel_Pile", "VP_FoldingLadder_Tall", "VP_Cable_Roll01",
			// Market (Utopia)
			"UT_MarketBasket01", "UT_MarketBasket02", "UT_MarketBasket03", "UT_MarketCrateSmall01", "UT_MarketCrateSmall02", "UT_MarketStackBox01", "UT_ClothOverhangBig04", "UT_ClothOverhangBig05",
			"UT_DreamCatcher01", "UT_DreamCatcher02", "UT_DanglyDecoration01", "UT_LightBottle01", "UT_CoveredCrate01", "UT_CoveredCrate02", "UT_DIY_Decoration01", "UT_Generator01",
			"Tangaroa_OutdoorFurnitureSilver_Bench", "Tangaroa_OutdoorFurnitureSilver_Table", "Tangaroa_OutdoorFurnitureSilver_Chair", "Tire_01",
			// Caravan outpost (Caravan Town)
			"Banner_01", "BenchTable_01", "PlasticChair_01", "Cableroll_01", "Cableroll_02", "Crate_Big_01", "Crate_Big_02", "Crate_Small_02", "Scaffolding_2x2m", "Scaffolding_6x4m",
			"LandmarkLadder_6m", "FishingNets_02", "MetalSheet_2", "MetalSheet_3", "PulauKafilahFlag", "RopeFence_Short", "Sign_01", "Sign_02", "Bench_01", "AcaciaTree_Big1", "DeadTree1", "DeadTree2",
			"CaravanIsland_BigRoundRock_1", "Candle_01", "MetalTable_01",
			// Bear country (Balboa)
			"Balboa_DecorationPrefabBase_Fence_Mid", "Balboa_DecorationPrefabBase_Fence_EndL", "Balboa_DecorationPrefabBase_Fence_EndR", "Balboa_DecorationPrefabBase_Fence_EndBroken",
			"Balboa_DecorationPrefabBase_Fence_Sign", "Balboa_DecorationPrefabBase_Lantern", "Balboa_DecorationPrefabBase_OldCouch", "Balboa_DecorationPrefabBase_Old Stove",
			"Balboa_DecorationPrefabBase_Wooden Spikes", "Balboa_DecorationPrefabBase_ToxicBarrel", "Balboa_DecorationPrefabBase_Generator", "Balboa_DecorationPrefabBase_Trashcan",
			"Balboa_DecorationPrefabBase_Table", "Balboa_DecorationPrefabBase_Chair", "BearSign1", "BearSign2 Variant", "Firewood_1", "Antenna_dish", "Balboa_DirectionSign",
			// Frozen camp (Temperance)
			"TP_Igloo_Small", "TP_Igloo_Small_1", "TP_Igloo_Medium_1", "TP_Igloo_Large_1", "TP_IcePillar01", "TP_IcePillar02", "TP_FoldingTable_Large", "TP_FoldingStool", "TP_Moontown_Barrel02",
			"TP_Moontown_SealedCrate02_Clean", "TP_Moontown_TarpCrate02_Clean", "SnowmobileShedMesh",
			// Hotel garden (Tangaroa)
			"LargePlant1", "LargePlant2", "MediumPlant1", "MediumPlant2", "SmallPlant1", "Flowerpot_01", "Flowerpot_02", "Flowerpot_03", "RestaSunshadeGround", "RestaFence", "Table_Common_01", "Dinnerchair_01",
			// Radio outpost (the radio tower)
			"RT_WindMill", "RT_SatteliteDisc", "RT_Floodlight_WithoutLightSource", "RT_Fence", "RT_SharkCage", "RT_PowerBox", "RT_Signs", "RT_Spotlight",
			// Floating rafts
			"campfire_1", "Scarecrow", "Bird_Nest", "Bed_Simple", "buoy",
			// Caves
			"TP_UnderwaterCaveTunnel_Straight", "TP_UnderwaterCaveTunnel_StraightLong", "TP_UnderwaterCaveTunnel_Corner", "TP_UnderwaterCaveTunnel_CornerLong", "TP_UnderwaterCaveTunnel_CrossT",
			"BalboaCave_Entrance", "BalboaCave_DeadEnd", "BalboaCave_Bear", "BalboaCave_Vines", "TP_CaveTunnel_SurfaceStaircase", "Ravine_Cave", "CaveVine1", "CaveVine2", "CaveVine3", "UndergroundToSurface_Tunnel",
			// Rocks for mounds over free-standing caves, and lights
			"BigBoulder1_Low", "BigBoulder2_Low", "BigBoulder3_Low", "BigBoulder4_Low", "SmallBoulder1", "SmallBoulder2", "SmallBoulder3", "SmallBoulder4", "BigRock_1", "BigRock_2", "BigRock_Low1_Sand",
			"TP_BigRock02", "TP_BigRock03", "TP_BigRock04", "BigSharpRock_1", "BigSharpRock_2", "CaravanIsland_BigRoundRock_1",
			"Placeable_Lantern_FireBasket", "Placeable_Lantern_Basic", "Placeable_Lantern_Metal", "Placeable_Lantern_Fireplace", "Placeable_StringLight_Horizontal", "Placeable_Utopia_LightBottleYellow",
		};

		[ConsoleCommand(name: "CIMeasureProps", docs: "Dev, main menu or editor: measures Raft's objects the randomizer builds with (size, bottom; the inside of cave pieces) into raft_props.txt in Mods\\DynamicIslands. CIMeasureProps [names, comma separated]")]
		public static void MeasurePropsCommand(string[] args)
		{
			string[] names = args != null && args.Length > 0 ? string.Join(" ", args).Split(',').Select(s => s.Trim()).ToArray() : MeasuredProps;
			DynamicIslands.instance.StartCoroutine(MeasureProps(names));
		}

		static IEnumerator MeasureProps(string[] names)
		{
			yield return PlaceableCatalog.EnsureBuilt();
			yield return PlaceableCatalog.EnsureLoaded(names);
			var inv = System.Globalization.CultureInfo.InvariantCulture;
			var lines = new List<string>
			{
				"# Raft's objects the randomizer builds with (CIMeasureProps, raft=" + Application.version + ")",
				"# prop\t<name>\t<scene>\t<size x,y,z>\t<centre x,y,z>\t<bottom below the pivot>\t<solid colliders>   (at the scale the object spawns with)",
				"# cave\t<name>\t<axis of the passage 0=x 2=z>\t<floor above the pivot>\t<headroom>\t<width>\t<open at +axis>\t<open at -axis>\t<a point in the passage, from the pivot>\t<room from it towards +axis>\t<and towards -axis> (99 = open)",
			};
			var lab = new GameObject("CI_PropLab");
			lab.transform.position = new Vector3(0f, 3000f, 0f);
			int measured = 0, missing = 0;
			foreach (string n in names)
			{
				GameObject proto = PlaceableCatalog.Get(n);
				Bounds b;
				if (proto == null || !PlaceableCatalog.LocalBounds(n, out b)) { Log("prop " + n + ": not in this Raft"); missing++; continue; }
				Vector3 s = proto.transform.localScale;
				var size = Vector3.Scale(b.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));
				var centre = Vector3.Scale(b.center, s);
				float bottom = b.min.y * s.y;
				int colliders = proto.GetComponentsInChildren<Collider>(true).Count(c => !c.isTrigger);
				lines.Add(string.Join("\t", new[] { "prop", n, PlaceableCatalog.SceneOf(n) ?? "", RaftProps.Text(size), RaftProps.Text(centre), bottom.ToString("0.###", inv), colliders.ToString(inv) }));
				measured++;
				if (!System.Text.RegularExpressions.Regex.IsMatch(n, "Cave|Tunnel|Ravine")) continue;

				// Cave pieces: where the passage is. Rays across it along x and along z at a few heights: a passage lets them
				// in (far, or all the way through); the floor is the highest thing a ray from inside finds below the middle.
				GameObject go = PlaceableCatalog.Spawn(n, lab.transform, false);
				if (go == null) continue;
				go.transform.localPosition = Vector3.zero;
				go.transform.localRotation = Quaternion.identity;
				yield return new WaitForFixedUpdate();
				Physics.SyncTransforms();
				Bounds w = new Bounds(go.transform.position, Vector3.zero);
				bool any = false;
				foreach (Collider c in go.GetComponentsInChildren<Collider>()) { if (c.isTrigger) continue; if (!any) { w = c.bounds; any = true; } else w.Encapsulate(c.bounds); }
				if (!any) { Log("cave " + n + ": no colliders"); UnityEngine.Object.Destroy(go); continue; }
				// (Raft's cave meshes face inwards: rays must see back faces to find them from outside or inside)
				bool backfaces = Physics.queriesHitBackfaces;
				Physics.queriesHitBackfaces = true;
				try
				{
					Func<Vector3, Vector3, float, float> depth = (from, dir, max) =>
					{
						float best = max;
						foreach (RaycastHit h in Physics.RaycastAll(from, dir, max, ~0, QueryTriggerInteraction.Ignore))
							if (h.collider.transform.IsChildOf(go.transform) && h.distance < best) best = h.distance;
						return best;
					};
					// A point inside the passage: a floor below and a roof above it, walls close on two sides, far or open on the others
					int bestAxis = -1; float bestAlong = 0f, bestWidth = 0f, bestHead = 0f, bestFloor = 0f, toPlus = 0f, toMinus = 0f; bool plus = false, minus = false;
					Vector3 bestAt = w.center;
					float reach = Mathf.Max(w.size.x, w.size.z) + 2f;
					foreach (Vector2 o in new[] { Vector2.zero, new Vector2(0.25f, 0f), new Vector2(-0.25f, 0f), new Vector2(0f, 0.25f), new Vector2(0f, -0.25f) })
						for (int k = 1; k <= 24; k++)
						{
							Vector3 q = new Vector3(w.center.x + o.x * w.size.x, w.min.y + w.size.y * k / 25f, w.center.z + o.y * w.size.z);
							float dd = depth(q, Vector3.down, w.size.y + 1f), du = depth(q, Vector3.up, w.size.y + 1f);
							if (dd > w.size.y || du > w.size.y || dd + du < 1.8f || dd > 3f) continue; // (standing height over its floor)
							float px = depth(q, Vector3.right, reach), nx = depth(q, Vector3.left, reach), pz = depth(q, Vector3.forward, reach), nz = depth(q, Vector3.back, reach);
							bool alongX = px + nx > pz + nz;
							float along = alongX ? px + nx : pz + nz, across = alongX ? pz + nz : px + nx;
							if (across < 1.5f || across > 20f) continue;
							if (along <= bestAlong + 0.01f) continue;
							bestAlong = along; bestAxis = alongX ? 0 : 2; bestWidth = across; bestHead = dd + du; bestFloor = q.y - dd - go.transform.position.y; bestAt = q;
							toPlus = Mathf.Min(alongX ? px : pz, 99f); toMinus = Mathf.Min(alongX ? nx : nz, 99f);
							// Open where a ray leaves the piece without meeting anything
							plus = alongX ? px >= reach - 0.01f || q.x + px > w.max.x + 0.4f : pz >= reach - 0.01f || q.z + pz > w.max.z + 0.4f;
							minus = alongX ? nx >= reach - 0.01f || q.x - nx < w.min.x - 0.4f : nz >= reach - 0.01f || q.z - nz < w.min.z - 0.4f;
						}
					if (bestAxis < 0) { Log("cave " + n + ": no passage found; size " + RaftProps.Text(size)); UnityEngine.Object.Destroy(go); continue; }
					lines.Add(string.Join("\t", new[] { "cave", n, bestAxis.ToString(inv), bestFloor.ToString("0.##", inv), bestHead.ToString("0.##", inv), bestWidth.ToString("0.##", inv), plus ? "1" : "0", minus ? "1" : "0",
						RaftProps.Text(bestAt - go.transform.position), toPlus.ToString("0.##", inv), toMinus.ToString("0.##", inv) }));
					Log("cave " + n + ": passage along " + (bestAxis == 0 ? "x" : "z") + " (" + bestAlong.ToString("0.0", inv) + " m seen), floor " + bestFloor.ToString("0.0", inv) + " m above the pivot, headroom " + bestHead.ToString("0.0", inv) +
						" m, width " + bestWidth.ToString("0.0", inv) + " m, open at " + (plus ? "+" : "") + (minus ? "-" : "") + (plus || minus ? "" : "neither") + "; size " + RaftProps.Text(size) + ", centre " + RaftProps.Text(centre) +
						", probe at " + RaftProps.Text(bestAt - go.transform.position));
				}
				finally { Physics.queriesHitBackfaces = backfaces; }
				UnityEngine.Object.Destroy(go);
			}
			UnityEngine.Object.Destroy(lab);
			string path = System.IO.Path.Combine(DynamicIslands.assetpath, RaftProps.FileName);
			System.IO.File.WriteAllLines(path, lines.ToArray());
			RaftProps.Reload();
			Log("Measured " + measured + " props (" + missing + " not in this Raft): " + System.IO.Path.GetFullPath(path));
			if (measured > 0) Log("PASS: measured props"); else Fail("measured no props");
		}

		[ConsoleCommand(name: "CIProbeSetPieces", docs: "Dev: size and footprint of Raft objects used as set pieces on oddity islands, and item names for rewards. CIProbeSetPieces [names, comma separated]")]
		public static void ProbeSetPiecesCommand(string[] args)
		{
			string[] names = args != null && args.Length > 0 ? string.Join(" ", args).Split(',').Select(s => s.Trim()).ToArray() :
				new[] { "Van_1", "Van_2", "Van3", "Van_4", "Van_5", "Caravan_Blue_01", "Caravan_Green_01", "Caravan_Green_02", "Caravan_Yellow_01", "CaravanBarebones1", "Airplane", "BoatStranded", "BoatMini",
					"Balboa_Shack", "Balboa_DecorationPrefabBase_SimpleTent", "CaravanRocket", "CaravanRocketDebris_Body1", "CaravanRocketDebris_Body2", "CaravanRocketDebris_Top1", "CaravanRocketDebris_Exhaust",
					"CaravanRocketDebris_Leg1", "CaravanRocketDebris_Door", "CaravanRocketDebris_Canister", "TangaroaFounderStatue", "RaftMonument", "Well", "RT_PlasticBoat", "RT_SharkCage", "Tire_02", "Pallet",
					"ReefHuts_Wall2", "ReefHuts_Tarpaulin", "Crate_Big_01", "Scarecrow", "RT_WindMill", "Scaffolding_2x2m" };
			DynamicIslands.instance.StartCoroutine(ProbeSetPieces(names));
		}

		static IEnumerator ProbeSetPieces(string[] names)
		{
			yield return PlaceableCatalog.EnsureBuilt();
			yield return PlaceableCatalog.EnsureLoaded(names);
			foreach (string n in names)
			{
				Bounds b;
				GameObject proto = PlaceableCatalog.Get(n);
				if (proto == null) { Log("piece " + n + ": not in the catalog"); continue; }
				if (!PlaceableCatalog.LocalBounds(n, out b)) { Log("piece " + n + ": no meshes"); continue; }
				Log("piece " + n + ": size " + b.size.ToString("F1") + " centre " + b.center.ToString("F1") + " bottom " + b.min.y.ToString("F2") + " scale " + proto.transform.localScale.ToString("F2") +
					" colliders " + proto.GetComponentsInChildren<Collider>(true).Count(c => !c.isTrigger));
			}
			var items = new List<string>();
			try { foreach (Item_Base i in ItemManager.GetAllItems()) items.Add(i.UniqueName); } catch (Exception e) { Log("items: " + e.Message); }
			Log("items " + items.Count + ": " + string.Join(" ", items.Where(i => System.Text.RegularExpressions.Regex.IsMatch(i, "Shark|Pork|Leather|Feather|Wool|Meat|Bear|Trophy|Head|Raw_|Hide|Tooth|Fur|Egg|Milk|Titan|Gold|Map", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray()));
			Log("PASS: probed set pieces");
		}

		static void ProbeLandmarks(float max)
		{
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			Network_Host_Entities host = ComponentManager<Network_Host_Entities>.Value;
			var agentOf = new Dictionary<AI_NetworkBehaviourType, int>();
			if (host != null && host.AINetworkBehaviourPrefabs != null)
				foreach (AI_NetworkBehaviour p in host.AINetworkBehaviourPrefabs)
				{
					if (p == null) continue;
					NavMeshAgent a = p.GetComponentInChildren<NavMeshAgent>(true);
					Log("prefab " + p.behaviourType + " " + p.GetType().Name + (a != null ? " agent " + a.agentTypeID + " (" + NavMesh.GetSettingsNameFromID(a.agentTypeID) + ")" : " no agent") +
						" scale " + p.localScaleInterval.minValue.ToString("0.##") + "-" + p.localScaleInterval.maxValue.ToString("0.##"));
					if (a != null) agentOf[p.behaviourType] = a.agentTypeID;
				}

			ChunkManager cm = ComponentManager<ChunkManager>.Value;
			if (cm != null)
				foreach (ChunkPoint cp in cm.GetAllChunkPointsList().Where(c => (c.worldPosition - raft).magnitude < max).OrderBy(c => (c.worldPosition - raft).magnitude))
					Log("point " + (cp.rule != null ? cp.rule.name : "?") + " at " + (cp.worldPosition - raft).magnitude.ToString("F0") + " m, type " + cp.rule?.ChunkPointType + ", remove at " + cp.RemoveDistanceFromRaft.ToString("F0") +
						", spawned " + (cp.spawnedObject != null ? cp.spawnedObject.name + " #" + cp.spawnedObject.ObjectIndex + " at " + (cp.spawnedObject.transform.position - cp.worldPosition).magnitude.ToString("F1") + " m from the point" : "nothing"));

			foreach (Landmark l in WorldManager.AllLandmarks.Where(l => l != null))
			{
				float d = (l.transform.position - raft).magnitude;
				if (!l.isSpawned || d > max) continue;
				var sb = new StringBuilder();
				PickupItem_Networked net = l.GetComponent<PickupItem_Networked>();
				sb.Append("landmark '").Append(l.name).Append("' unique ").Append(l.uniqueLandmarkIndex).Append(" #").Append(net != null ? net.ObjectIndex.ToString() : "-").Append(" at ").Append(d.ToString("F0")).Append(" m");
				Log(sb.ToString());
				if (l.landmarkItems != null)
					foreach (var g in l.landmarkItems.Where(i => i != null).GroupBy(i => i.GetType().Name + " " + PlaceableCatalog.CleanName(i.name)).OrderByDescending(g => g.Count()))
						Log("  item " + g.Key + " x" + g.Count() + " (y " + string.Join(",", g.Take(4).Select(i => (i.transform.position.y).ToString("F0")).ToArray()) + ")");
				if (l.spawners != null)
					foreach (LandmarkEntitySpawner s in l.spawners.Where(s => s != null))
					{
						NavMeshHit hit;
						int agent;
						bool nav = agentOf.TryGetValue(s.entityType, out agent) && NavMesh.SamplePosition(s.transform.position, out hit, 6f, new NavMeshQueryFilter { agentTypeID = agent, areaMask = NavMesh.AllAreas });
						Log("  spawner " + s.GetType().Name + " " + s.entityType + " [" + string.Join(",", (s.spawnabeEntityTypes ?? new AI_NetworkBehaviourType[0]).Select(t => t.ToString()).ToArray()) + "] should " + s.ShouldSpawn +
							" has " + (s.spawnedEntityBehaviour != null) + " nav " + nav + " at " + s.transform.localPosition.ToString("F0"));
					}
				// NavMesh per agent kind around the island
				foreach (var kv in agentOf)
				{
					int found = 0;
					for (int i = 0; i < 40; i++)
					{
						float a = i * 2.4f, r = 10f + (i % 8) * 12f;
						Vector3 p = l.transform.position + new Vector3(Mathf.Cos(a) * r, 30f, Mathf.Sin(a) * r);
						NavMeshHit hit;
						if (NavMesh.SamplePosition(p, out hit, 60f, new NavMeshQueryFilter { agentTypeID = kv.Value, areaMask = NavMesh.AllAreas })) found++;
					}
					if (found > 0) Log("  navmesh for " + kv.Key + ": " + found + "/40 probes");
				}
			}

			foreach (AI_NetworkBehaviour ai in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
			{
				var mats = ai.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).SelectMany(r => r.sharedMaterials).Where(m => m != null).ToList();
				Log("animal " + ai.behaviourType + " " + ai.GetType().Name + " #" + ai.ObjectIndex + " at " + (ai.transform.position - raft).magnitude.ToString("F0") + " m, scale " + ai.transform.localScale.x.ToString("0.##") +
					", spawner " + (ai.connectedSpawner != null ? ai.connectedSpawner.name + (ai.connectedSpawner.landmark != null ? " of " + ai.connectedSpawner.landmark.name : "") : "none") +
					", health " + (ai.networkEntity != null && ai.networkEntity.stat_health != null ? ai.networkEntity.stat_health.Max.ToString("F0") : "?") +
					", materials " + string.Join(", ", mats.Select(m => m.name + "/" + m.shader.name + (m.HasProperty("_Color") ? "" : " (no _Color)")).Distinct().Take(4).ToArray()));
			}
			foreach (AI_NetworkBehaviour ai in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().Where(a => a.behaviourType == AI_NetworkBehaviourType.Shark))
			{
				Log("shark #" + ai.ObjectIndex + " active " + ai.gameObject.activeInHierarchy + " at " + ai.transform.position.ToString("F0") + ", children " + ai.transform.childCount);
				foreach (Renderer r in ai.GetComponentsInChildren<Renderer>(true))
					Log("  renderer " + r.GetType().Name + " '" + r.name + "' enabled " + r.enabled + " active " + r.gameObject.activeInHierarchy + ": " + string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => m.name + "/" + m.shader.name).ToArray()));
			}
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(r => r.name.IndexOf("shark", StringComparison.OrdinalIgnoreCase) >= 0 || (r.sharedMaterial != null && r.sharedMaterial.name.IndexOf("shark", StringComparison.OrdinalIgnoreCase) >= 0)))
				Log("shark-like renderer '" + r.name + "' under '" + (r.transform.root != null ? r.transform.root.name : "") + "' at " + (r.transform.position - raft).magnitude.ToString("F0") + " m: " + string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => m.name + "/" + m.shader.name).ToArray()));
			Log("PASS: probed");
		}
	}
}
