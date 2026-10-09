using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Tests of the world options (World settings in the New Game box): scrambled blueprints, story islands in a new order,
	/// ghost rafts, private storages - alone, together in every combination, and for every player (mpoptions.ps1).
	/// </summary>
	public static partial class DevTests
	{
		#region Measuring Raft's story islands

		static readonly string[] StoryScenes = { "RadioTower", "Vasagatan", "Balboa", "Caravan", "Tangaroa", "Varuna", "Temperance", "Utopia" };

		[ConsoleCommand(name: "CIMeasureBlueprints", docs: "Dev, main menu or editor: loads each of Raft's story island scenes (switched off) and lists the blueprints lying on them (pickups that give a Blueprint_ item; quest items and notes left out) into Mods\\DynamicIslands\\raft_blueprints.txt - shipped with the mod for the world option Scrambled blueprints")]
		public static void MeasureBlueprintsCommand() { StartTest(MeasureBlueprintsRoutine()); }

		static IEnumerator MeasureBlueprintsRoutine()
		{
			var lines = new List<string> { "# Blueprints lying on Raft's story islands (CIMeasureBlueprints, Raft " + Application.version + "): island|item|pickup", };
			var scenes = PlaceableCatalog.LandmarkSceneNames().Where(s => StoryScenes.Any(k => s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
			Log("Story island scenes: " + string.Join(", ", scenes.ToArray()));
			int found = 0;
			foreach (string scene in scenes)
			{
				string island = StoryScenes.First(k => scene.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
				int here = 0;
				yield return PlaceableCatalog.VisitScene(scene, s => ReadBlueprints(s, island, lines, n => here += n));
				found += here;
				Log("  " + scene + ": " + here + " blueprint pickup(s)");
			}
			string path = Path.Combine(DynamicIslands.assetpath, ScrambledBlueprints.FileName);
			File.WriteAllLines(path, lines.ToArray());
			ScrambledBlueprints.Read(true);
			Log("Wrote " + path + ": " + found + " pickups, " + ScrambledBlueprints.OnIslands.Count + " different blueprints, " + ScrambledBlueprints.Movable.Count + " may move");
			if (found > 0) Log("PASS: blueprints measured"); else Fail("blueprints measured: none found");
		}

		static IEnumerator ReadBlueprints(Scene scene, string island, List<string> lines, Action<int> count)
		{
			int n = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
				foreach (PickupItem p in root.GetComponentsInChildren<PickupItem>(true))
				{
					if (p == null || p.pickupItemType == PickupItemType.QuestItem || p.pickupItemType == PickupItemType.NoteBookNote) continue;
					var items = new List<Item_Base>();
					if (p.itemInstance != null && p.itemInstance.baseItem != null) items.Add(p.itemInstance.baseItem);
					if (p.yieldHandler != null && p.yieldHandler.yieldAsset != null && p.yieldHandler.yieldAsset.yieldAssets != null)
						items.AddRange(p.yieldHandler.yieldAsset.yieldAssets.Where(c => c != null && c.item != null).Select(c => c.item));
					foreach (Item_Base i in items.Where(i => i.UniqueName.StartsWith("Blueprint_")))
					{
						lines.Add(island + "|" + i.UniqueName + "|" + p.name);
						n++;
					}
				}
			count(n);
			yield break;
		}

		#endregion

		#region Units

		[ConsoleCommand(name: "CIWorldOptionsUnit", docs: "Dev, anywhere: the world options' rules without a world - the options' text both ways; the story order for 300 seeds (all eight islands once, Utopia last, never Raft's own order, the same for the same seed, a mapping both ways); the blueprints' pairs for 100 seeds (every movable blueprint given once, none keeps its own, what the story needs never in them, the same for the same seed); 300 ghost rafts (small, medium and large about as often as meant, a large one with rats, screechers, a hoard and a note, every object one the mod knows); the last choice kept in world_rules.txt")]
		public static void WorldOptionsUnitCommand() { StartTest(WorldOptionsUnitRoutine()); }

		static IEnumerator WorldOptionsUnitRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			// The options' text
			HashSet<string> parsed = WorldOptions.Parse("storyorder, nonsense,GHOSTRAFTS;;privatestorage");
			Check(ref ok, parsed.Count == 3 && parsed.Contains(WorldOptions.StoryOrder) && parsed.Contains(WorldOptions.GhostRafts) && parsed.Contains(WorldOptions.PrivateStorage), "options read from text, unknown ones left out: " + WorldOptions.Describe(parsed));
			Check(ref ok, WorldOptions.Encode(WorldOptions.All, 42) == "on=blueprints,storyorder,ghostrafts,privatestorage,longvoyage,ironraft,sharedxp,nightdanger;seed=42" && WorldOptions.Encode(new string[0], 7) == "on=;seed=7", "options written as text: " + WorldOptions.Encode(WorldOptions.All, 42));
			// Long voyage: twice as many of Raft's islands between random custom islands (and twice the spacing)
			{
				bool was = WorldOptions.Current.Contains(WorldOptions.LongVoyage);
				int since = WorldIslands.RaftIslandsSince, target = WorldIslands.Target;
				var plain = new List<int>(); var longer = new List<int>();
				WorldOptions.Current.Remove(WorldOptions.LongVoyage);
				for (int i = 0; i < 200; i++) { WorldIslands.NewTarget(); plain.Add(WorldIslands.Target); }
				WorldOptions.Current.Add(WorldOptions.LongVoyage);
				for (int i = 0; i < 200; i++) { WorldIslands.NewTarget(); longer.Add(WorldIslands.Target); }
				string gap = WorldIslands.DescribeGap();
				if (!was) WorldOptions.Current.Remove(WorldOptions.LongVoyage);
				WorldIslands.RaftIslandsSince = since; WorldIslands.Target = target;
				Check(ref ok, longer.Min() == plain.Min() * 2 && longer.Max() == plain.Max() * 2 && longer.Average() > plain.Average() * 1.8f, "Long voyage: random custom islands after twice as many of Raft's islands (" + plain.Min() + "-" + plain.Max() + " -> " + longer.Min() + "-" + longer.Max() + ")");
				Check(ref ok, gap.Contains("Long voyage") && WorldOptions.Label(WorldOptions.LongVoyage) == "Long voyage" && !WorldOptions.IsRetired(WorldOptions.LongVoyage), "Long voyage: offered, and the gap says so (" + gap + ")");
			}
			// The story order
			var badOrders = new List<string>();
			var firsts = new HashSet<ChunkPointType>();
			for (int seed = 1; seed <= 300; seed++)
			{
				ChunkPointType[] o = StoryOrder.OrderFor(seed * 7919);
				firsts.Add(o[0]);
				bool good = o.Length == 8 && o.Distinct().Count() == 8 && o.All(t => StoryOrder.Chain.Contains(t)) && o[7] == ChunkPointType.Landmark_Utopia &&
					!o.SequenceEqual(StoryOrder.Chain) && o.SequenceEqual(StoryOrder.OrderFor(seed * 7919));
				if (!good) badOrders.Add(seed + ": " + StoryOrder.Describe(o));
			}
			Check(ref ok, badOrders.Count == 0, "300 story orders: all eight islands once, Utopia last, never Raft's own, the same again" + (badOrders.Count > 0 ? " - not: " + string.Join("; ", badOrders.Take(3).ToArray()) : ""));
			Check(ref ok, firsts.Count >= 5, "... and " + firsts.Count + " different islands come first (" + string.Join(", ", firsts.Select(StoryOrder.Name).ToArray()) + ")");
			Log("  e.g. seed 12345: " + StoryOrder.Describe(StoryOrder.OrderFor(12345)));
			// The blueprints' pairs
			List<string> movable = ScrambledBlueprints.Movable;
			Check(ref ok, ScrambledBlueprints.OnIslands.Count > 0, ScrambledBlueprints.FileName + ": " + ScrambledBlueprints.OnIslands.Count + " blueprints on the story islands, " + movable.Count + " may move (" + string.Join(", ", movable.Take(8).ToArray()) + (movable.Count > 8 ? "..." : "") + ")");
			Check(ref ok, !movable.Any(b => ScrambledBlueprints.Keep.Contains(b)), "what the story needs never moves (" + string.Join(", ", ScrambledBlueprints.Keep.Where(k => ScrambledBlueprints.OnIslands.ContainsKey(k)).ToArray()) + " kept)");
			var badPairs = new List<string>();
			int sameIsland = 0, pairsSeen = 0;
			for (int seed = 1; seed <= 100 && movable.Count > 1; seed++)
			{
				Dictionary<string, string> p = ScrambledBlueprints.PairsFor(seed * 104729, movable);
				bool good = p.Count == movable.Count && p.Values.Distinct().Count() == movable.Count && p.Values.All(v => movable.Contains(v)) && p.All(kv => kv.Key != kv.Value) &&
					p.OrderBy(kv => kv.Key).SequenceEqual(ScrambledBlueprints.PairsFor(seed * 104729, movable).OrderBy(kv => kv.Key));
				if (!good) badPairs.Add(seed.ToString());
				foreach (var kv in p)
				{
					pairsSeen++;
					List<string> a, b;
					if (ScrambledBlueprints.OnIslands.TryGetValue(kv.Key, out a) && ScrambledBlueprints.OnIslands.TryGetValue(kv.Value, out b) && a.Intersect(b).Any()) sameIsland++;
				}
			}
			Check(ref ok, badPairs.Count == 0, "100 seeds of pairs: each movable blueprint given once, none keeps its place, the same again" + (badPairs.Count > 0 ? " - not seeds " + string.Join(", ", badPairs.Take(5).ToArray()) : ""));
			Log("  " + sameIsland + " of " + pairsSeen + " pairs stay on the same island (only where nothing else fits)");
			// Ghost rafts
			var sizes = new int[3];
			var badRafts = new List<string>();
			for (int i = 0; i < 300; i++)
			{
				IslandGenSettings s = GhostRafts.Settings(new System.Random(i * 31 + 5));
				IslandFile f = GhostRafts.Build(s, "cighost-" + i);
				int size = GhostRafts.SizeOf(s.Seed);
				sizes[size]++;
				Func<string, int> count = n => f.Objects.Count(o => o.Name == n);
				var unknown = f.Objects.Select(o => o.Name).Distinct().Where(n => !n.StartsWith("Creature_") && !PlaceableCatalog.IsLoaded(n)).ToList();
				var why = new List<string>();
				if (count("Block_Foundation") < 3) why.Add(count("Block_Foundation") + " foundations");
				if (unknown.Count > 0) why.Add("unknown " + string.Join(",", unknown.ToArray()));
				if (count("Note_Bottle") + count("Note_Paper") < 1) why.Add("no note");
				if (count("Loot_Barrel") < 1) why.Add("no barrel");
				if (size == GhostRafts.Large)
				{
					if (count("Creature_Rat") < 2) why.Add(count("Creature_Rat") + " rat spots");
					if (count("Creature_StoneBird") < 1) why.Add("no screecher");
					if (!f.Objects.Any(o => o.Name == "Loot_Chest" && ObjectProps.Get(o.Props, ObjectProps.NoteTitle) == "Ghost raft hoard")) why.Add("no hoard");
					if (count("Block_Foundation") < 30) why.Add(count("Block_Foundation") + " foundations");
				}
				if (why.Count > 0) badRafts.Add(i + " (" + GhostRafts.SizeNames[size] + ": " + string.Join(", ", why.ToArray()) + ")");
			}
			Check(ref ok, badRafts.Count == 0, "300 ghost rafts: foundations, a barrel and a note on each; each large one with rats, screechers and a hoard; every object known" + (badRafts.Count > 0 ? " - not: " + string.Join("; ", badRafts.Take(4).ToArray()) : ""));
			Check(ref ok, sizes[0] > 140 && sizes[0] < 220 && sizes[1] > 50 && sizes[2] > 15 && sizes[2] < 60, "their sizes: " + sizes[0] + " small, " + sizes[1] + " medium, " + sizes[2] + " large (about 60 / 28 / 12 %)");
			// The last choice
			string rules = Path.Combine(DynamicIslands.assetpath, WorldRules.DefaultFileName);
			string before = File.Exists(rules) ? File.ReadAllText(rules) : null;
			try
			{
				WorldOptions.SaveDefaults(new[] { WorldOptions.GhostRafts, WorldOptions.LongVoyage });
				HashSet<string> d = WorldOptions.Defaults;
				Check(ref ok, d.Count == 2 && d.Contains(WorldOptions.GhostRafts) && d.Contains(WorldOptions.LongVoyage) && WorldRules.ReadDefault("monsters") == (before != null ? WorldRules.ReadDefault("monsters") : null), "the last choice kept in world_rules.txt (" + WorldOptions.Describe(d) + "), the other lines untouched");
				WorldOptions.SaveDefaults(new string[0]);
				Check(ref ok, WorldOptions.Defaults.Count == 0, "... and none");
			}
			finally { if (before != null) File.WriteAllText(rules, before); else if (File.Exists(rules)) File.Delete(rules); }
			if (ok) Log("PASS: world options rules"); else Fail("world options rules");
		}

		#endregion

		#region The New Game box

		[ConsoleCommand(name: "CIWorldSettingsBox", docs: "Dev, main menu: Raft's New Game box's World settings clicked as a player does: the button opens the window over the box, each option's button switches it on and off (its label says so, the box's button counts them), All off, Done closes it and keeps the choice. Leaves the options named chosen for the next world: CIWorldSettingsBox [option ...] (default none)")]
		public static void WorldSettingsBoxCommand(string[] args) { StartTest(WorldSettingsBoxRoutine(WorldOptions.Parse(string.Join(",", args ?? new string[0])))); }

		static IEnumerator WorldSettingsBoxRoutine(HashSet<string> want)
		{
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("World settings box: no New Game box (main menu?)"); yield break; }
			bool ok = true;
			box.gameObject.SetActive(true);
			try { box.Close(); } catch { } box.Open(); // (Raft's Open subscribes to input changes each time, Close unsubscribes: never open twice)
			yield return new WaitForSecondsRealtime(1f);
			Button open = WorldSettingsWindow.OpenButton;
			Check(ref ok, open != null && open.gameObject.activeInHierarchy, "the box has its World settings button ('" + (open != null ? UIKit.LabelOf(open).text : "") + "')");
			if (open == null) { Fail("World settings box"); yield break; }
			open.onClick.Invoke();
			yield return null;
			Check(ref ok, WorldSettingsWindow.IsOpen, "the button opens the window");
			// (its three groups: the world rules, the randomizer, the extra options)
			var names = WorldSettingsWindow.Window.GetComponentsInChildren<RectTransform>(false).Select(r => r.name).ToList();
			Check(ref ok, names.Contains(NewWorldRulesBox.PanelName) && names.Contains("CustomIslands_Randomizer") && WorldOptions.Offered.All(o => names.Contains("Option_" + o)) && !WorldOptions.Retired.Any(o => names.Contains("Option_" + o)), "the window has the world rules, the world randomizer and the " + WorldOptions.Offered.Length + " extra options");
			int levelBefore = NewWorldRulesBox.MonsterLevel, percentBefore = NewWorldRulesBox.BuildPercent;
			RandomizerSettings randBefore = NewWorldOptions.Randomizer.Copy();
			Screenshot(new[] { "worldsettings" });
			yield return new WaitForSecondsRealtime(0.6f);
			var wrong = new List<string>();
			foreach (string o in WorldOptions.Offered)
			{
				Button t = WorldSettingsWindow.Toggle(o);
				if (t == null) { wrong.Add(o + ": no button"); continue; }
				bool was = WorldSettingsWindow.Chosen.Contains(o);
				t.onClick.Invoke(); yield return null;
				bool now = WorldSettingsWindow.Chosen.Contains(o);
				string label = UIKit.LabelOf(t).text;
				if (now == was || !label.EndsWith(now ? "ON" : "off")) wrong.Add(o + " (" + label + ")");
				t.onClick.Invoke(); yield return null;
				if (WorldSettingsWindow.Chosen.Contains(o) != was) wrong.Add(o + " back");
			}
			Check(ref ok, wrong.Count == 0, "each option's button switches it on and off, its label says which" + (wrong.Count > 0 ? " - not: " + string.Join(", ", wrong.ToArray()) : ""));
			foreach (string o in WorldOptions.Offered) if (!WorldSettingsWindow.Chosen.Contains(o)) { WorldSettingsWindow.Toggle(o).onClick.Invoke(); yield return null; }
			Check(ref ok, WorldSettingsWindow.Chosen.Count == WorldOptions.Offered.Length && UIKit.LabelOf(open).text.Contains(WorldSettingsWindow.Changed + " changed"), "all on: the box's button says '" + UIKit.LabelOf(open).text + "'");
			Check(ref ok, ClickIn(WorldSettingsWindow.Window, "Raft's own"), "Raft's own clicked");
			yield return null;
			Check(ref ok, WorldSettingsWindow.Chosen.Count == 0 && !NewWorldOptions.Randomizer.On && NewWorldRulesBox.MonsterLevel == MonsterDifficulty.Normal && NewWorldRulesBox.BuildPercent == 0 && UIKit.LabelOf(open).text.Contains(WorldIslands.Chosen.Count > 0 ? "1 changed" : "Raft's own"), // (islands left out are this PC's list: kept)
				"Raft's own: no options, no randomizer, Normal monsters, Raft's build cost ('" + UIKit.LabelOf(open).text + "')");
			// (the rules and the randomizer as they were: only the options are this test's choice)
			NewWorldRulesBox.MonsterLevel = levelBefore; NewWorldRulesBox.BuildPercent = percentBefore;
			NewWorldOptions.Randomizer.Level = randBefore.Level; NewWorldOptions.Randomizer.Disabled.Clear(); foreach (string d in randBefore.Disabled) NewWorldOptions.Randomizer.Disabled.Add(d);
			NewWorldOptions.Refresh();
			foreach (string o in want) { WorldSettingsWindow.Toggle(o).onClick.Invoke(); yield return null; }
			Check(ref ok, ClickIn(WorldSettingsWindow.Window, "Done"), "Done clicked");
			yield return null;
			Check(ref ok, !WorldSettingsWindow.IsOpen && WorldSettingsWindow.Chosen.SetEquals(want) && WorldOptions.Pending != null && WorldOptions.Pending.SetEquals(want), "Done closes it; chosen for the next world: " + WorldOptions.Describe(WorldSettingsWindow.Chosen));
			// (every control of the window on the screen, and inside the box)
			open.onClick.Invoke(); yield return null; yield return null;
			var off = OffScreen(WorldSettingsWindow.Window.gameObject, Screen.width, Screen.height);
			Check(ref ok, off.Count == 0, "the window fits the screen (" + Screen.width + "x" + Screen.height + ")" + (off.Count > 0 ? " - off: " + string.Join(", ", off.Take(4).ToArray()) : ""));
			WorldSettingsWindow.Close();
			box.gameObject.SetActive(false);
			if (ok) Log("PASS: world settings box"); else Fail("world settings box");
		}

		static bool ClickIn(RectTransform root, string label) { return root != null && Click(root.gameObject, label); }

		[ConsoleCommand(name: "CIWorldOptionsCheck", docs: "Dev, in game (either player): the world has exactly these options (and a seed); logs OPTIONS and SERVER lines: CIWorldOptionsCheck [option ...]")]
		public static void WorldOptionsCheckCommand(string[] args)
		{
			HashSet<string> want = WorldOptions.Parse(string.Join(",", args ?? new string[0]));
			bool ok = true;
			Check(ref ok, LoadSceneManager.IsGameSceneLoaded, "in a world");
			Check(ref ok, WorldOptions.Current.SetEquals(want), "the world's options: " + WorldOptions.Describe(WorldOptions.Current) + " (wanted " + WorldOptions.Describe(want) + ")");
			Check(ref ok, want.Count == 0 || WorldOptions.Seed != 0, "its seed: " + WorldOptions.Seed);
			Log("OPTIONS " + WorldOptions.Encode(WorldOptions.Current, WorldOptions.Seed) + " | order " + StoryOrder.Describe(StoryOrder.Order) + " | storages " + PrivateStorage.Count);
			if (ok) Log("PASS: world options check"); else Fail("world options check");
		}

		#endregion

		#region Ghost rafts

		[ConsoleCommand(name: "CIGhostRafts", docs: "Dev, world (host): ghost rafts come while sailing only with the option on (40 km simulated, as CIRandomizerSail: none with it off, several with it on, none within the first 1.5 km); then a large one brought ahead: loaded, its hoard, barrels and note there, its rats on its deck and its screechers in the air, the player standing on its deck. CIGhostRafts [keep] (keep: the large one stays, 'Ghost raft' extras file logged as GHOST <name>)")]
		public static void GhostRaftsCommand(string[] args) { StartTest(GhostRaftsRoutine(args != null && args.Contains("keep"))); }

		static IEnumerator GhostRaftsRoutine(bool keep)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("ghost rafts: host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			for (int i = 0; i < 6 && ChunkManager.RaftIsInsideChunkPoint; i++) yield return SailRoutine(20f, 20f);
			Vector3 raft = CustomIslandSpawner.RaftPosition.Value;
			Func<bool, IEnumerator> sail = null;
			int brought = 0;
			var sizes = new List<string>();
			sail = on =>
			{
				return SailGhosts(on, raft, n => brought = n, sizes);
			};
			try
			{
				yield return sail(false);
				Check(ref ok, brought == 0, "the option off: 40 km, no ghost raft");
				yield return sail(true);
				Check(ref ok, brought >= 4, "the option on: 40 km, " + brought + " ghost rafts (" + string.Join(", ", sizes.ToArray()) + ")");
				// A large one, ahead of the raft
				var s = new IslandGenSettings { Height = 2f };
				int seed = 1;
				while (GhostRafts.SizeOf(seed) != GhostRafts.Large) seed++;
				s.Seed = seed; s.Radius = 26f;
				string name = CustomIslandSpawner.GeneratedPrefix + GhostRafts.TypeName + "-" + seed;
				IslandFile f = MapTypes.Create(MapTypes.Get(GhostRafts.TypeName), s, 0f, name);
				f.Save(IslandSpawner.PathFor(name));
				Vector3? spot = CustomIslandSpawner.FindClearSpot(CustomIslandSpawner.RaftPosition.Value, 40f, 160f);
				if (!spot.HasValue) { Fail("ghost rafts: no open sea near the raft"); yield break; }
				int before = IslandWorldState.Islands.Count;
				yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
				IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
				Check(ref ok, e != null && e.Root != null, "a large ghost raft brought (" + name + ")");
				if (e != null && e.Root != null)
				{
					Log("GHOST " + e.HostName);
					yield return WaitFor(() => AnimalsOf(e, "Rat").Count >= 2 && AnimalsOf(e, "Screecher").Count >= 1, 60f);
					List<AI_NetworkBehaviour> rats = AnimalsOf(e, "Rat"), birds = AnimalsOf(e, "Screecher");
					float deck = e.Root.transform.position.y + f.WaterLevel;
					Check(ref ok, rats.Count >= 2 && birds.Count >= 1, "its guards: " + rats.Count + " rat(s), " + birds.Count + " screecher(s)");
					Check(ref ok, rats.All(r => Mathf.Abs(r.transform.position.y - deck) < 4f), "the rats on its deck (heights " + string.Join(", ", rats.Select(r => (r.transform.position.y - deck).ToString("F1")).ToArray()) + " m from the sea)");
					Check(ref ok, e.Root.GetComponentsInChildren<LootCrate>(true).Any(c => { IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>(); return r != null && ObjectProps.Get(r.Props, ObjectProps.NoteTitle) == "Ghost raft hoard"; }), "its hoard is there");
					Check(ref ok, e.Root.GetComponentsInChildren<CustomNote>(true).Any(n => n.Title == "Captain's log"), "its captain's log is there");
					// (a raft has no land: the player is put on its deck, beside the hoard, and must stay standing on it)
					LootCrate hoard = e.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault(c => { IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>(); return r != null && ObjectProps.Get(r.Props, ObjectProps.NoteTitle) == "Ghost raft hoard"; });
					Network_Player player = RAPI.GetLocalPlayer();
					if (hoard != null && player != null)
					{
						// (a clear tile of the deck: nothing above it but the sky - not inside a hut or on the loot)
						Vector3 at = hoard.transform.position + new Vector3(0f, 1.2f, 0f);
						foreach (BoxCollider d in e.Root.GetComponentsInChildren<BoxCollider>(true).Where(x => x.name == "CustomIslands_Deck"))
						{
							Vector3 top = new Vector3(d.bounds.center.x, d.bounds.max.y, d.bounds.center.z);
							RaycastHit hit;
							if (Physics.Raycast(top + Vector3.up * 6f, Vector3.down, out hit, 7f, ~0, QueryTriggerInteraction.Ignore) && hit.collider == d) { at = top + Vector3.up * 1.2f; break; }
						}
						yield return PutPlayer(player, at, false);
						// (the guards may knock a player off soon after: standing on the deck at some moment in the first two seconds)
						bool stood = false; string seen = "";
						for (int i = 0; i < 8 && !stood; i++)
						{
							yield return new WaitForSeconds(0.25f);
							Collider under = player.PersonController.groundRaycastHit.collider;
							stood = player.PersonController.IsGrounded && under != null && under.name == "CustomIslands_Deck";
							seen = (player.PersonController.IsGrounded ? "grounded" : "in the air") + " on " + (under != null ? under.name : "nothing") + ", " + (at.y - player.transform.position.y).ToString("F1") + " m below where put";
						}
						Check(ref ok, stood, "the player stands on its deck (" + seen + ")");
						OnRaftCommand();
					}
					if (!keep) IslandWorldState.RemoveIds(new List<int> { e.Id }, true);
				}
			}
			finally { WorldOptions.Set(optionsBefore); }
			if (ok) Log("PASS: ghost rafts"); else Fail("ghost rafts");
		}

		static IEnumerator SailGhosts(bool on, Vector3 raft, Action<int> result, List<string> sizes)
		{
			var opts = new HashSet<string>(WorldOptions.Current);
			if (on) opts.Add(WorldOptions.GhostRafts); else opts.Remove(WorldOptions.GhostRafts);
			WorldOptions.Set(opts);
			GhostRafts.Reset();
			var known = new HashSet<int>(IslandWorldState.Islands.Select(e => e.Id));
			int n = 0, early = 0;
			for (int i = 0; i < 160; i++)
			{
				GhostRafts.OnSailed(250f, raft);
				foreach (IslandWorldState.Entry e in IslandWorldState.Islands.Where(e => !known.Contains(e.Id)).ToList())
				{
					known.Add(e.Id);
					if (!(e.HostName ?? "").Contains(GhostRafts.TypeName)) continue;
					n++;
					if (i * 250f < 1500f) early++;
					int seed;
					string[] p = e.HostName.Split('-');
					if (int.TryParse(p[p.Length - 1], out seed)) sizes.Add(GhostRafts.SizeNames[GhostRafts.SizeOf(seed)]);
					IslandWorldState.RemoveIds(new List<int> { e.Id }, true);
				}
				if (i % 8 == 0) yield return null;
			}
			if (early > 0) Fail("ghost rafts: " + early + " within the first 1.5 km");
			result(n);
		}

		#endregion

		[ConsoleCommand(name: "CIDeckProbe", docs: "Dev, in game: what a player would stand on across a custom island without land (a raft of blocks): rays down on a grid around its middle - what they hit and how high above the sea: CIDeckProbe <island>")]
		public static void DeckProbeCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			float sea = 0f;
			var seen = new List<string>();
			for (float dx = -9f; dx <= 9f; dx += 3f)
				for (float dz = -9f; dz <= 9f; dz += 3f)
				{
					Vector3 from = new Vector3(e.Position.x + dx, 30f, e.Position.z + dz);
					RaycastHit hit;
					if (Physics.Raycast(from, Vector3.down, out hit, 60f, ~0, QueryTriggerInteraction.Ignore))
						seen.Add(dx.ToString("F0") + "," + dz.ToString("F0") + ": " + hit.collider.name + " (layer " + LayerMask.LayerToName(hit.collider.gameObject.layer) + ") at " + (hit.point.y - sea).ToString("F2") + " m");
					else seen.Add(dx.ToString("F0") + "," + dz.ToString("F0") + ": nothing");
				}
			foreach (string s in seen) Log("DECK " + s);
			Log("PASS: deck probe");
		}

		[ConsoleCommand(name: "CIRayLine", docs: "Dev, in game: what stands along a line on a custom island, for a walk that sticks - at <n> points from <x1> <z1> to <x2> <z2> (m from its middle, as play tests give them) everything a ray down from 40 m meets and its height above the sea: CIRayLine <island> <x1> <z1> <x2> <z2> <n>")]
		public static void RayLineCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			if (args.Length < 6) { Fail("CIRayLine <island> <x1> <z1> <x2> <z2> <n>"); return; }
			Func<int, float> num = i => float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture);
			float x1 = num(1), z1 = num(2), x2 = num(3), z2 = num(4);
			int n = Mathf.Max(2, Mathf.RoundToInt(num(5)));
			for (int i = 0; i < n; i++)
			{
				float f = i / (float)(n - 1), x = Mathf.Lerp(x1, x2, f), z = Mathf.Lerp(z1, z2, f);
				Vector3 from = new Vector3(e.Position.x + x, 40f, e.Position.z + z);
				RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore).OrderByDescending(h => h.point.y).ToArray();
				Log("RAY " + x.ToString("F2") + " " + z.ToString("F2") + ": " + (hits.Length == 0 ? "nothing" :
					string.Join(", ", hits.Select(h => h.collider.name + (h.collider.GetComponent<Terrain>() != null ? " (terrain)" : "") + " " + h.point.y.ToString("F2")).ToArray())));
			}
			Log("PASS: ray line");
		}

		[ConsoleCommand(name: "CIRaySlice", docs: "Dev, in game: a doorway's opening on a custom island - from <x> <z> (m from its middle) rays toward <x2> <z2>, one every 25 cm of height from <h1> to <h2> above the sea: how far each goes and what stops it: CIRaySlice <island> <x> <z> <x2> <z2> <h1> <h2>")]
		public static void RaySliceCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			if (args.Length < 7) { Fail("CIRaySlice <island> <x> <z> <x2> <z2> <h1> <h2>"); return; }
			Func<int, float> num = i => float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture);
			Vector3 a = new Vector3(e.Position.x + num(1), 0f, e.Position.z + num(2)), b = new Vector3(e.Position.x + num(3), 0f, e.Position.z + num(4));
			Vector3 dir = (b - a).normalized;
			float reach = Vector3.Distance(a, b);
			for (float h = num(5); h <= num(6) + 0.001f; h += 0.25f)
			{
				RaycastHit hit;
				bool got = Physics.Raycast(new Vector3(a.x, h, a.z), dir, out hit, reach, ~0, QueryTriggerInteraction.Ignore);
				Log("SLICE h=" + h.ToString("F2") + ": " + (got ? hit.collider.name + (hit.collider.GetComponent<Terrain>() != null ? " (terrain)" : "") + " at " + hit.distance.ToString("F2") + " m" : "clear for " + reach.ToString("F1") + " m"));
			}
			Log("PASS: ray slice");
		}

		[ConsoleCommand(name: "CIRaftDeckProbe", docs: "Dev, in game: Raft's own raft - for a few foundations: the block's height, the top of what a player stands on above it (Raft's raft collider), and the block's own colliders (their bounds) - to give the mod's rafts of blocks the same floor")]
		public static void RaftDeckProbeCommand()
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null) { Fail("raft deck probe: no raft"); return; }
			foreach (Block b in raft.GetComponentsInChildren<Block>().Where(x => (x.name.Contains("Foundation") || (x.buildableItem != null && x.buildableItem.UniqueName.Contains("Foundation"))) || x.name.Contains("Floor")).Take(6))
			{
				Vector3 p = b.transform.position;
				RaycastHit hit;
				string top = Physics.Raycast(p + Vector3.up * 5f + new Vector3(0.3f, 0f, 0.3f), Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore)
					? hit.collider.name + " (" + LayerMask.LayerToName(hit.collider.gameObject.layer) + ") top " + (hit.point.y - p.y).ToString("F3") + " m above the block" : "nothing";
				string own = string.Join("; ", b.GetComponentsInChildren<Collider>(true).Select(c => c.name + "/" + LayerMask.LayerToName(c.gameObject.layer) + (c.isTrigger ? "/trigger" : "") + " y " + (c.bounds.min.y - p.y).ToString("F2") + ".." + (c.bounds.max.y - p.y).ToString("F2") + " size " + c.bounds.size.x.ToString("F2") + "x" + c.bounds.size.z.ToString("F2") + (c.enabled ? "" : " off")).ToArray());
				Log("RAFTDECK " + b.name + " at y " + p.y.ToString("F2") + " (sea 0): stands on " + top + " | its colliders: " + own);
			}
			Log("PASS: raft deck probe");
		}

		[ConsoleCommand(name: "CIStandAt", docs: "Dev, in game: puts the local player at an island's middle plus x, z, this high above the sea, and logs every half second where they are, whether grounded and on what: CIStandAt <island> <x> <z> [height, default 1.5]")]
		public static void StandAtCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			float x = 0f, z = 0f, h = 1.5f;
			if (args.Length > 1) float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x);
			if (args.Length > 2) float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
			if (args.Length > 3) float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out h);
			StartTest(StandAtRoutine(new Vector3(e.Position.x + x, h, e.Position.z + z)));
		}

		static IEnumerator StandAtRoutine(Vector3 at)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			yield return PutPlayer(player, at, false);
			for (int i = 0; i < 8; i++)
			{
				yield return new WaitForSeconds(0.5f);
				PersonController pc = player.PersonController;
				Collider ground = pc.groundRaycastHit.collider;
				Log(string.Format("STAND t={0:F1}s y={1:F2} grounded={2} mode={3} on {4} (layer {5})", (i + 1) * 0.5f, player.transform.position.y, pc.IsGrounded, pc.controllerType,
					ground != null ? ground.name : "nothing", ground != null ? LayerMask.LayerToName(ground.gameObject.layer) : "-"));
			}
			Log("PASS: stand at");
		}

		#region Story order

		[ConsoleCommand(name: "CIStoryOrderWorld", docs: "Dev, world (host): the story order in a world - with the option on, Raft unlocking the first frequency (the Receiver's note) unlocks the order's first island, the next note its second, and so on; the notebook's list rebuilt from the notes found when the option changes (off: Raft's islands again, on: the order's); the frequency numbers on notes follow. What the world had unlocked is put back after")]
		public static void StoryOrderWorldCommand() { StartTest(StoryOrderWorldRoutine()); }

		static IEnumerator StoryOrderWorldRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("story order: host, in a world"); yield break; }
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			var unlockedBefore = NoteBook.unlockedChunkPointType.ToList();
			try
			{
				var on = new HashSet<string>(WorldOptions.Current) { WorldOptions.StoryOrder };
				WorldOptions.Set(on);
				yield return null;
				ChunkPointType[] order = StoryOrder.Order;
				Check(ref ok, StoryOrder.Active && order[7] == ChunkPointType.Landmark_Utopia && !order.SequenceEqual(StoryOrder.Chain), "the option on: the order " + StoryOrder.Describe(order));
				// The story played along the order: the Receiver's note first, then on each island the note Raft put there
				// (the one that unlocks Raft's next island after it): each must unlock the order's next - all of them, in turn
				var got = new List<string>();
				bool chain = true;
				var reached = new List<ChunkPointType>();
				ChunkPointType at = ChunkPointType.None;
				for (int step = 0; step < StoryOrder.Chain.Length; step++)
				{
					// (Raft's note found here: the Receiver's unlocks Chain[0]; on island Chain[k] lies the one for Chain[k+1])
					int k = at == ChunkPointType.None ? -1 : Array.IndexOf(StoryOrder.Chain, at);
					if (k + 1 >= StoryOrder.Chain.Length) { chain = false; got.Add(StoryOrder.Name(at) + " has no note (Utopia is the end)"); break; }
					ChunkPointType noteFor = StoryOrder.Chain[k + 1];
					NoteBook.unlockedChunkPointType.RemoveAll(t => StoryOrder.Chain.Contains(t));
					NoteBook.UnlockFrequency(noteFor);
					List<ChunkPointType> now = NoteBook.unlockedChunkPointType.Where(t => StoryOrder.Chain.Contains(t)).ToList();
					got.Add((at == ChunkPointType.None ? "the Receiver" : StoryOrder.Name(at)) + " > " + string.Join(",", now.Select(StoryOrder.Name).ToArray()));
					if (now.Count != 1 || now[0] != order[step]) { chain = false; break; }
					at = now[0];
					reached.Add(at);
				}
				Check(ref ok, chain && reached.SequenceEqual(order), "the story played along the order reaches every island in turn: " + string.Join("; ", got.ToArray()));
				Dictionary<int, ChunkPointType> notes = StoryOrder.FrequencyNotes();
				Log("  Raft's frequency notes: " + string.Join(", ", notes.Select(kv => kv.Key + ">" + StoryOrder.Name(kv.Value)).ToArray()));
				Check(ref ok, notes.Count >= 7, notes.Count + " notes that unlock a story island found in the notebook");
				// Rebuilt from the notes found: the first frequency's note (the Receiver's) and the next one
				var found = notes.OrderBy(kv => Array.IndexOf(StoryOrder.Chain, kv.Value)).Take(2).ToList();
				var indexesBefore = NoteBook.unlockedNoteBookIndexes.ToList();
				try
				{
					foreach (var kv in found) if (!NoteBook.unlockedNoteBookIndexes.Contains(kv.Key)) NoteBook.unlockedNoteBookIndexes.Add(kv.Key);
					StoryOrder.Rebuild();
					var withOrder = NoteBook.unlockedChunkPointType.Where(t => StoryOrder.Chain.Contains(t)).OrderBy(t => t).ToList();
					var wantOrder = found.Select(kv => StoryOrder.Map(kv.Value)).OrderBy(t => t).ToList();
					Check(ref ok, withOrder.SequenceEqual(wantOrder), "the list rebuilt from the notes found: " + string.Join(", ", withOrder.Select(StoryOrder.Name).ToArray()) + " (what those two notes unlock in this order)");
					WorldOptions.Set(new HashSet<string>(optionsBefore.Where(o => o != WorldOptions.StoryOrder)));
					var raftOwn = NoteBook.unlockedChunkPointType.Where(t => StoryOrder.Chain.Contains(t)).OrderBy(t => t).ToList();
					Check(ref ok, raftOwn.SequenceEqual(found.Select(kv => kv.Value).OrderBy(t => t)), "the option off: Raft's own islands for the same notes (" + string.Join(", ", raftOwn.Select(StoryOrder.Name).ToArray()) + ")");
					WorldOptions.Set(on);
					string text = StoryOrder.FrequencyText(StoryOrder.Chain[1]);
					RecieverFrequency own = RecieverFrequency.AllFrequencies != null ? RecieverFrequency.AllFrequencies.FirstOrDefault(x => x != null && x.chunkPointType == StoryOrder.Map(StoryOrder.Chain[1])) : null;
					Check(ref ok, text != null && own != null && text == own.ToString(), "the number on Radio Tower's note (Raft's way to Vasagatan) is the frequency of the island after Radio Tower in this order, " + StoryOrder.Name(StoryOrder.Map(StoryOrder.Chain[1])) + " (" + text + ")");
				}
				finally
				{
					NoteBook.unlockedNoteBookIndexes.Clear();
					NoteBook.unlockedNoteBookIndexes.AddRange(indexesBefore);
				}
			}
			finally
			{
				WorldOptions.Set(optionsBefore);
				NoteBook.unlockedChunkPointType.Clear();
				NoteBook.unlockedChunkPointType.AddRange(unlockedBefore);
			}
			if (ok) Log("PASS: story order in a world"); else Fail("story order in a world");
		}

		[ConsoleCommand(name: "CIStoryUnlock", docs: "Dev, in game (host): unlocks the frequency notes a player finds along this world's story order (the Receiver's, then the one on each island reached) for every player, as finding them would (NoteBook.UnlockSpecificNoteNetworked): CIStoryUnlock <how many> - only in a test world named 'CI ...'")]
		public static void StoryUnlockCommand(string[] args)
		{
			if (!Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("story unlock: host, in a test world 'CI ...'"); return; }
			int n;
			if (args == null || args.Length == 0 || !int.TryParse(args[0], out n)) n = 1;
			NoteBook book = UnityEngine.Object.FindObjectOfType<NoteBook>();
			if (book == null) { Fail("story unlock: no notebook"); return; }
			// (the notes a player finds along this world's order: the Receiver's, then the one lying on each island reached)
			Dictionary<ChunkPointType, int> noteFor = StoryOrder.FrequencyNotes().ToDictionary(kv => kv.Value, kv => kv.Key);
			var notes = new List<KeyValuePair<int, ChunkPointType>>();
			ChunkPointType at = ChunkPointType.None;
			for (int i = 0; i < n; i++)
			{
				int k = at == ChunkPointType.None ? -1 : Array.IndexOf(StoryOrder.Chain, at);
				if (k + 1 >= StoryOrder.Chain.Length || !noteFor.ContainsKey(StoryOrder.Chain[k + 1])) break;
				ChunkPointType raftType = StoryOrder.Chain[k + 1];
				notes.Add(new KeyValuePair<int, ChunkPointType>(noteFor[raftType], raftType));
				at = StoryOrder.Map(raftType);
			}
			foreach (var kv in notes) book.UnlockSpecificNoteNetworked(kv.Key, false);
			Log("Unlocked the notes " + string.Join(", ", notes.Select(kv => kv.Key + " (Raft's " + StoryOrder.Name(kv.Value) + ", here " + StoryOrder.Name(StoryOrder.Map(kv.Value)) + ")").ToArray()));
			Log("PASS: story unlock");
		}

		#endregion

		#region Private storages

		// (how high above a foundation a storage stands, as Raft places one on it)
		static float StorageHeight = 0.6f;

		static Storage_Small StorageByIndex(string arg)
		{
			uint idx;
			if (!uint.TryParse(arg ?? "", out idx)) return null;
			// (from the scene: Raft empties its own list, StorageManager.allStorages, on some scene events)
			return UnityEngine.Object.FindObjectsOfType<Storage_Small>().FirstOrDefault(s => s != null && s.ObjectIndex == idx);
		}

		static Network_Player OtherPlayer()
		{
			Network_Player local = RAPI.GetLocalPlayer();
			return UnityEngine.Object.FindObjectsOfType<Network_Player>().FirstOrDefault(p => p != null && p != local);
		}


		[ConsoleCommand(name: "CIStorages", docs: "Dev, in game: every storage in the scene - its index, its builder (world option Private storages) and its height on the raft: STORED lines")]
		public static void StoragesCommand()
		{
			foreach (Storage_Small s in UnityEngine.Object.FindObjectsOfType<Storage_Small>())
				Log("STORED " + s.ObjectIndex + " builder " + PrivateStorage.BuilderOf(s.ObjectIndex) + " local y " + s.transform.localPosition.y.ToString("F2"));
			Log("PASS: storages listed");
		}
		/// <summary>The storage CIPlaceStorage placed last (null when it failed).</summary>
		internal static Block LastPlacedStorage;
		[ConsoleCommand(name: "CIPlaceStorage", docs: "Dev, in game (host): places a small storage on the raft as a player builds one (through that player's BlockCreator, sent to every player): CIPlaceStorage [host|other] [height above the foundation] - logs STORAGE <index> by <player id>")]
		public static void PlaceStorageCommand(string[] args)
		{
			LastPlacedStorage = null;
			if (!Raft_Network.IsHost) { Fail("place storage: host only"); return; }
			Network_Player who = args != null && args.Length > 0 && args[0] == "other" ? OtherPlayer() : RAPI.GetLocalPlayer();
			float up = StorageHeight;
			if (args != null && args.Length > 1) float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out up);
			if (who == null) { Fail("place storage: no such player"); return; }
			Item_Base item = ItemManager.GetItemByName("Placeable_Storage_Small");
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			List<Block> floors = raft != null ? raft.GetComponentsInChildren<Block>().Where(x => (x.name.Contains("Foundation") || (x.buildableItem != null && x.buildableItem.UniqueName.Contains("Foundation")))).OrderBy(x => x.transform.localPosition.sqrMagnitude).ToList() : new List<Block>();
			// (the next foundation for each storage, round again on a small raft)
			Block floor = floors.Count > 0 ? floors[UnityEngine.Object.FindObjectsOfType<Storage_Small>().Length % floors.Count] : null;
			if (item == null || floor == null) { Fail("place storage: " + (item == null ? "no storage item" : "no foundation on the raft (" + (raft != null ? raft.GetComponentsInChildren<Block>().Length : 0) + " blocks)")); return; }
			Block b = who.BlockCreator.CreateBlockCheat(item, floor.transform.localPosition + new Vector3(0f, up, 0f), Vector3.zero, DPS.Default, 0);
			if (b == null) { Fail("place storage: Raft didn't place it"); return; }
			LastPlacedStorage = b;
			Log("STORAGE " + b.ObjectIndex + " by " + who.steamID.Id + " (builder noted: " + PrivateStorage.BuilderOf(b.ObjectIndex) + ")");
			Log("PASS: storage placed");
		}

		[ConsoleCommand(name: "CIOpenStorage", docs: "Dev, in game (either player): this player tries to open a storage as with E (the look first: is it refused?, then Raft's StorageManager.OpenStorage), closes it again: CIOpenStorage <index> - logs OPEN <index> opened|refused")]
		public static void OpenStorageCommand(string[] args)
		{
			Storage_Small s = StorageByIndex(args != null && args.Length > 0 ? args[0] : "");
			Network_Player me = RAPI.GetLocalPlayer();
			if (s == null || me == null) { Fail("open storage: no storage " + (args != null && args.Length > 0 ? args[0] : "")); return; }
			bool looksRefused = PrivateStorage.Refuses(s, me);
			bool opened = me.StorageManager.OpenStorage(s);
			if (opened) me.StorageManager.CloseStorage(s);
			Log("OPEN " + s.ObjectIndex + " " + (opened ? "opened" : "refused") + " (the look: " + (looksRefused ? "someone else's" : "may open") + "; builder " + PrivateStorage.BuilderOf(s.ObjectIndex) + ", me " + me.steamID.Id + ")");
			if (opened == looksRefused) Fail("open storage: the look and the opening disagree");
			else Log("PASS: open storage");
		}

		[ConsoleCommand(name: "CIOpenStorageAs", docs: "Dev, in game (host): the other player's request to open a storage as the host handles it (their StorageManager on the host): CIOpenStorageAs <index> - logs OPENAS <index> opened|refused")]
		public static void OpenStorageAsCommand(string[] args)
		{
			Storage_Small s = StorageByIndex(args != null && args.Length > 0 ? args[0] : "");
			Network_Player other = OtherPlayer();
			if (s == null || other == null || !Raft_Network.IsHost) { Fail("open storage as: host, another player and a storage"); return; }
			bool opened = other.StorageManager.OpenStorage(s);
			if (opened) other.StorageManager.CloseStorage(s);
			Log("OPENAS " + s.ObjectIndex + " " + (opened ? "opened" : "refused"));
			Log("PASS: open storage as");
		}

		[ConsoleCommand(name: "CIPrivateStorage", docs: "Dev, world (host, single player): the option Private storages alone - a storage built with the option on is the builder's (noted, saved in the world file's line); the builder opens it; one noted for another player (as if they built it) is refused for a third player, and the host may open it while that builder is not in the game (T9); the hammer does not take it down; with the option off both open; a storage built with it off has no builder; storages removed after")]
		public static void PrivateStorageCommand() { StartTest(PrivateStorageRoutine()); }

		static IEnumerator PrivateStorageRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("private storage: host, in a world"); yield break; }
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			var made = new List<Block>();
			string storagesBefore = PrivateStorage.Encode();
			try
			{
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.PrivateStorage });
				Network_Player me = RAPI.GetLocalPlayer();
				// (placed through the network: the new storage shows up a frame or two later)
				HashSet<Storage_Small> had = new HashSet<Storage_Small>(UnityEngine.Object.FindObjectsOfType<Storage_Small>());
				Storage_Small last = null;
				Func<Storage_Small> newest = () => UnityEngine.Object.FindObjectsOfType<Storage_Small>().FirstOrDefault(x => !had.Contains(x));
				Action place = () => { PlaceStorageCommand(new string[0]); last = LastPlacedStorage as Storage_Small ?? newest(); };
				Action took = () => { if (last != null) { made.Add(last); had.Add(last); } };
				place();
				for (int f = 0; f < 60 && last == null; f++) { yield return null; last = newest(); }
				took();
				Block mine = last;
				Check(ref ok, mine != null && PrivateStorage.BuilderOf(mine.ObjectIndex) == me.steamID.Id, "a storage built with the option on is the builder's (" + (mine != null ? mine.ObjectIndex.ToString() : "none") + ")");
				Check(ref ok, WorldOptions.WriteLines().Any(l => l.StartsWith("@storages=") && mine != null && l.Contains(mine.ObjectIndex + ":" + me.steamID.Id)), "... kept in the world file's line");
				Storage_Small ms = mine as Storage_Small;
				bool opened = ms != null && me.StorageManager.OpenStorage(ms);
				if (opened) me.StorageManager.CloseStorage(ms);
				Check(ref ok, opened, "its builder opens it");
				place();
				for (int f = 0; f < 60 && last == null; f++) { yield return null; last = newest(); }
				took();
				Block theirs = last;
				Storage_Small ts = theirs as Storage_Small;
				if (ts != null) PrivateStorage.Decode(PrivateStorage.Encode() + ";" + ts.ObjectIndex + ":12345");
				// (another player in the game is refused; the host may open one whose builder isn't in the game - ROADMAP T9)
				bool refused = ts != null && !PrivateStorage.MayOpen(ts.ObjectIndex, 999UL);
				Check(ref ok, refused, "one built by another player is refused for a third player");
				bool hostOpens = ts != null && me.StorageManager.OpenStorage(ts);
				if (hostOpens) me.StorageManager.CloseStorage(ts);
				Check(ref ok, hostOpens && !PrivateStorage.Refuses(ts, me), "... and the host opens it while its builder isn't in the game (T9)");
				// (AU42: the hammer - Raft's RemovePlaceables.PickupBlock - doesn't take it down either)
				RemovePlaceables hammer = me.GetComponentInChildren<RemovePlaceables>(true);
				if (hammer != null && ts != null) HarmonyLib.Traverse.Create(hammer).Method("PickupBlock", new[] { typeof(Block) }).GetValue(ts);
				yield return null;
				Check(ref ok, hammer != null && ts != null && StorageManager.allStorages.Contains(ts), "the hammer doesn't take it down (" + (hammer == null ? "no hammer" : "") + ")");
				WorldOptions.Set(new HashSet<string>(optionsBefore.Where(o => o != WorldOptions.PrivateStorage)));
				bool both = ms != null && ts != null && me.StorageManager.OpenStorage(ts);
				if (both) me.StorageManager.CloseStorage(ts);
				Check(ref ok, both, "the option off: it opens");
				place();
				for (int f = 0; f < 60 && last == null; f++) { yield return null; last = newest(); }
				took();
				Block free = last;
				Check(ref ok, free != null && PrivateStorage.BuilderOf(free.ObjectIndex) == 0UL, "a storage built with the option off has no builder (" + (free == null ? "none placed" : free.ObjectIndex + ": " + PrivateStorage.BuilderOf(free.ObjectIndex)) + ")");
			}
			finally
			{
				foreach (Block b in made) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
				PrivateStorage.Decode(storagesBefore);
				WorldOptions.Set(optionsBefore);
			}
			if (ok) Log("PASS: private storage"); else Fail("private storage");
		}

		[ConsoleCommand(name: "CIIronRaft", docs: "Dev, world (host, single player): the option Iron raft - a shark's bite (Raft's Block.Damage) takes half from a foundation with the option on (bites of 6, 5, 5 take 3, 2, 3), the whole bite with it off; the hammer still takes a block down with it on; health and options put back after")]
		public static void IronRaftCommand() { StartTest(IronRaftRoutine()); }

		static IEnumerator IronRaftRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("iron raft: host, in a world"); yield break; }
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			var made = new List<Block>();
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			Block floor = raft != null ? raft.GetComponentsInChildren<Block>().Where(x => (x.name.Contains("Foundation") || (x.buildableItem != null && x.buildableItem.UniqueName.Contains("Foundation")))).OrderByDescending(x => HarmonyLib.Traverse.Create(x).Field("health").GetValue<int>()).FirstOrDefault() : null;
			if (floor == null) { Fail("iron raft: no foundation on the raft"); yield break; }
			var health = HarmonyLib.Traverse.Create(floor).Field("health");
			int start = health.GetValue<int>();
			try
			{
				if (start < 30) health.SetValue(30);
				int h0 = health.GetValue<int>();
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.IronRaft });
				IronRaft.Reset();
				var took = new List<int>();
				foreach (int bite in new[] { 6, 5, 5 }) { int before = health.GetValue<int>(); floor.Damage(bite); took.Add(before - health.GetValue<int>()); }
				Check(ref ok, took.SequenceEqual(new[] { 3, 2, 3 }), "the option on: bites of 6, 5, 5 take " + string.Join(", ", took.Select(x => x.ToString()).ToArray()) + " (half, the odd half carried)");
				health.SetValue(h0);
				WorldOptions.Set(new HashSet<string>(optionsBefore.Where(o => o != WorldOptions.IronRaft)));
				floor.Damage(6);
				int offTook = h0 - health.GetValue<int>();
				Check(ref ok, offTook == 6, "the option off: a bite of 6 takes " + offTook);
				health.SetValue(h0);
				// (the user: taking pieces down with the hammer or the axe must stay as it was)
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.IronRaft });
				Network_Player me = RAPI.GetLocalPlayer();
				HashSet<Storage_Small> had = new HashSet<Storage_Small>(UnityEngine.Object.FindObjectsOfType<Storage_Small>());
				PlaceStorageCommand(new string[0]);
				Storage_Small s = LastPlacedStorage as Storage_Small;
				// (placed through the network: it shows up a frame or two later)
				for (int f = 0; f < 60 && s == null; f++) { yield return null; s = UnityEngine.Object.FindObjectsOfType<Storage_Small>().FirstOrDefault(x => !had.Contains(x)); }
				bool placed = s != null;
				if (placed) made.Add(s);
				RemovePlaceables hammer = me != null ? me.GetComponentInChildren<RemovePlaceables>(true) : null;
				if (hammer != null && s != null) HarmonyLib.Traverse.Create(hammer).Method("PickupBlock", new[] { typeof(Block) }).GetValue(s);
				yield return null;
				yield return null;
				Check(ref ok, hammer != null && placed && s == null, "the option on: the hammer still takes a block down (" + (hammer == null ? "no hammer" : !placed ? "nothing placed" : s != null ? "still there" : "gone") + ")");
			}
			finally
			{
				foreach (Block b in made) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
				if (floor != null) health.SetValue(start);
				IronRaft.Reset();
				WorldOptions.Set(optionsBefore);
			}
			if (ok) Log("PASS: iron raft"); else Fail("iron raft");
		}

		[ConsoleCommand(name: "CINightDanger", docs: "Dev, world (host): the option Night is dangerous - at night a monster's bite on a player x1.3 and a hit on a monster /1.3 on top of the difficulty, by day x0.85; the shark's search interval x0.6 at night, x1.25 by day; the option off: as before; options and difficulty put back after")]
		public static void NightDangerCommand() { StartTest(NightDangerRoutine()); }

		static IEnumerator NightDangerRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("night danger: host, in a world"); yield break; }
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			int difficultyBefore = MonsterDifficulty.Current;
			var holder = new GameObject("CINightDanger");
			try
			{
				Func<string, EntityType, Network_Entity> make = (n, type) =>
				{
					var go = new GameObject(n);
					go.transform.SetParent(holder.transform, false);
					Network_Entity ne = go.AddComponent<Network_Entity>();
					ne.entityType = type;
					return ne;
				};
				Network_Entity player = make("player", EntityType.Player), enemy = make("enemy", EntityType.Enemy);
				AI_StateMachine_Shark shark = UnityEngine.Object.FindObjectOfType<AI_StateMachine_Shark>();
				Func<float> interval = () => shark != null ? shark.SearchBlockInterval : -1f;
				MonsterDifficulty.Current = MonsterDifficulty.Normal;
				WorldOptions.Set(new HashSet<string>(optionsBefore.Where(o => o != WorldOptions.NightDanger)));
				NightDanger.TestNight = true;
				float plainBite = MonsterDifficulty.Scale(player, 20f, EntityType.Enemy), plainShark = interval();
				Check(ref ok, Mathf.Abs(plainBite - 20f) < 0.001f, "the option off: a bite at night as before (" + plainBite + ")");
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.NightDanger });
				float nightBite = MonsterDifficulty.Scale(player, 20f, EntityType.Enemy), nightHit = MonsterDifficulty.Scale(enemy, 26f, EntityType.Player), nightShark = interval();
				NightDanger.TestNight = false;
				float dayBite = MonsterDifficulty.Scale(player, 20f, EntityType.Enemy), dayShark = interval();
				Check(ref ok, Mathf.Abs(nightBite - 26f) < 0.01f && Mathf.Abs(nightHit - 20f) < 0.01f && Mathf.Abs(dayBite - 17f) < 0.01f, "monsters: a bite of 20 at night " + nightBite + ", a hit of 26 on one at night " + nightHit + ", a bite by day " + dayBite);
				MonsterDifficulty.Current = MonsterDifficulty.Normal + 1;
				NightDanger.TestNight = true;
				float fierce = MonsterDifficulty.Scale(player, 20f, EntityType.Enemy);
				Check(ref ok, Mathf.Abs(fierce - 20f * MonsterDifficulty.Factor * NightDanger.MonsterNight) < 0.01f, "on top of the difficulty (" + MonsterDifficulty.Name(MonsterDifficulty.Current) + " at night: " + fierce + ")");
				Check(ref ok, shark != null && Mathf.Abs(nightShark - plainShark * NightDanger.SharkNight) < 0.01f && Mathf.Abs(dayShark - plainShark * NightDanger.SharkDay) < 0.01f,
					"the shark looks for the raft every " + plainShark.ToString("F1") + " s -> " + nightShark.ToString("F1") + " s at night, " + dayShark.ToString("F1") + " s by day" + (shark == null ? " (no shark)" : ""));
				NightDanger.TestNight = null;
				Log("Now: " + (NightDanger.IsNight ? "night" : "day") + " by Raft's sky");
			}
			finally
			{
				NightDanger.TestNight = null;
				MonsterDifficulty.Current = difficultyBefore;
				WorldOptions.Set(optionsBefore);
				UnityEngine.Object.Destroy(holder);
			}
			yield return null;
			if (ok) Log("PASS: night danger"); else Fail("night danger");
		}

		#endregion

		#region Scrambled blueprints

		[ConsoleCommand(name: "CIBlueprintsWorld", docs: "Dev, world (host, a test world 'CI Options ...' only - it brings one of Raft's story islands): with the option on, the story island with the most movable blueprints is brought near the raft (Raft's own ChunkManager.AddChunkPointForcibly) and sailed to; each of its blueprint pickups gives its partner (item and name), none of what the story needs changes; the option off: Raft's own again, on: the partners again")]
		public static void BlueprintsWorldCommand() { StartTest(BlueprintsWorldRoutine()); }

		static readonly Dictionary<string, ChunkPointType> StoryTypes = new Dictionary<string, ChunkPointType>
		{
			{ "RadioTower", ChunkPointType.Landmark_RadioTower }, { "Vasagatan", ChunkPointType.Landmark_Vasagatan }, { "Balboa", ChunkPointType.Landmark_Balboa },
			{ "Caravan", ChunkPointType.Landmark_CaravanIsland }, { "Tangaroa", ChunkPointType.Landmark_Tangaroa }, { "Varuna", ChunkPointType.Landmark_VarunaPoint },
			{ "Temperance", ChunkPointType.Landmark_Temperance }, { "Utopia", ChunkPointType.Landmark_Utopia },
		};

		static IEnumerator BlueprintsWorldRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI Options")) { Fail("blueprints in a world: host, in a test world 'CI Options ...'"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			try
			{
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.Blueprints });
				List<string> movable = ScrambledBlueprints.Movable;
				// The story island with the most movable blueprints
				string island = StoryTypes.Keys.OrderByDescending(k => movable.Count(b => ScrambledBlueprints.OnIslands[b].Contains(k))).First();
				int expected = movable.Count(b => ScrambledBlueprints.OnIslands[b].Contains(island));
				Check(ref ok, expected > 0, "the story island with the most blueprints that move: " + island + " (" + expected + ")");
				ChunkManager chunks = ComponentManager<ChunkManager>.Value ?? UnityEngine.Object.FindObjectOfType<ChunkManager>();
				ChunkPoint point = chunks != null ? chunks.AddChunkPointForcibly(StoryTypes[island]) : null;
				Check(ref ok, point != null, "Raft brings " + island + (point != null ? " " + Flat(point.worldPosition - CustomIslandSpawner.RaftPosition.Value).magnitude.ToString("F0") + " m away" : ""));
				if (point == null) yield break;
				Landmark l = null;
				Func<Landmark> find = () => WorldManager.AllLandmarks.FirstOrDefault(x => x != null && x.isSpawned && x.name.IndexOf(island, StringComparison.OrdinalIgnoreCase) >= 0);
				// (Raft shifts the world back to its middle as the raft moves: the island's point follows those shifts - whether
				// Raft moves its point too or not)
				Vector3 p0 = point.worldPosition, shifted = Vector3.zero;
				Action<Vector3> onShift = s => shifted += s;
				WorldShiftManager.OnWorldShift += onShift;
				Func<Vector3> target = () => point.worldPosition != p0 ? point.worldPosition : p0 + shifted;
				try
				{
					for (int leg = 0; leg < 6 && (l = find()) == null; leg++)
					{
						yield return SailTo(() => target() + (CustomIslandSpawner.RaftPosition.Value - target()).normalized * 200f, 60f);
						yield return WaitFor(() => (l = find()) != null, 20f);
					}
				}
				finally { WorldShiftManager.OnWorldShift -= onShift; }
				yield return WaitFor(() => (l = find()) != null, 60f);
				Check(ref ok, l != null, island + " spawned");
				if (l == null) yield break;
				yield return new WaitForSeconds(4f);
				ScrambledBlueprints.Rebuild();
				yield return new WaitForSeconds(2f);
				Func<List<string>> items = () => l.GetComponentsInChildren<PickupItem>(true).SelectMany(p =>
				{
					var list = new List<string>();
					if (p.itemInstance != null && p.itemInstance.baseItem != null) list.Add(p.itemInstance.baseItem.UniqueName);
					var y = p.yieldHandler != null ? HarmonyLib.Traverse.Create(p.yieldHandler).Field("yield").GetValue<List<Cost>>() : null;
					if (y != null) list.AddRange(y.Where(c => c != null && c.item != null).Select(c => c.item.UniqueName));
					return list;
				}).Where(n => n.StartsWith("Blueprint_")).OrderBy(n => n).ToList();
				List<string> on = items();
				WorldOptions.Set(new HashSet<string>(optionsBefore.Where(o => o != WorldOptions.Blueprints)));
				yield return new WaitForSeconds(1.5f);
				List<string> raftOwn = items();
				WorldOptions.Set(new HashSet<string>(optionsBefore) { WorldOptions.Blueprints });
				yield return new WaitForSeconds(1.5f);
				List<string> again = items();
				Log("  Raft's own: " + string.Join(", ", raftOwn.ToArray()));
				Log("  scrambled:  " + string.Join(", ", on.ToArray()));
				var wantOn = raftOwn.Select(ScrambledBlueprints.Map).OrderBy(n => n).ToList();
				Check(ref ok, on.SequenceEqual(wantOn) && raftOwn.Any(b => ScrambledBlueprints.Map(b) != b), "each blueprint pickup gives its partner (" + raftOwn.Count(b => ScrambledBlueprints.Map(b) != b) + " changed)");
				Check(ref ok, raftOwn.Where(b => ScrambledBlueprints.Keep.Contains(b)).All(b => on.Contains(b)), "what the story needs stays (" + string.Join(", ", raftOwn.Where(b => ScrambledBlueprints.Keep.Contains(b)).ToArray()) + ")");
				Check(ref ok, again.SequenceEqual(on), "off and on again: Raft's own, then the partners again");
			}
			finally { WorldOptions.Set(optionsBefore); }
			if (ok) Log("PASS: blueprints in a world"); else Fail("blueprints in a world");
		}

		#endregion


		#region The experimental release box

		[ConsoleCommand(name: "CIExperimentalNotice", docs: "Dev, main menu: the alpha notice (EXPERIMENTAL ALPHA RELEASE) - there with its header, the mod's version and its three points; at 8 screen sizes on the screen and clear of Raft's menu buttons and of the New Game box (opened); Got it folds it and remembers it for this version, Show opens it again and forgets it; its help buttons (Discord, Guide (PDF), Report a problem and its box), dragging it (kept on the screen, remembered, reset); its state before is put back; pictures shot_notice_*")]
		public static void ExperimentalNoticeCommand() { StartTest(ExperimentalNoticeRoutine()); }

		static Rect ScreenRect(RectTransform r)
		{
			var c = new Vector3[4];
			r.GetWorldCorners(c);
			return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
		}

		static IEnumerator ExperimentalNoticeRoutine()
		{
			GameObject canvas = GameObject.Find("MainMenuCanvas");
			RectTransform panel = ExperimentalNotice.Panel;
			if (canvas == null || panel == null) { Fail("experimental notice: no main menu or no box"); yield break; }
			bool ok = true;
			bool foldedBefore = ExperimentalNotice.Folded;
			string placeBefore = ExperimentalNotice.SavedPlace; // (the checks are made where the box starts)
			ExperimentalNotice.ResetPlace();
			if (foldedBefore) { ExperimentalNotice.Flip(); yield return null; }
			string text = string.Join(" | ", panel.GetComponentsInChildren<Text>(true).Select(t => t.text).ToArray());
			Check(ref ok, text.Contains(ExperimentalNotice.Header) && ExperimentalNotice.Points.All(p => text.Contains(p)) && text.Contains("(version " + ExperimentalNotice.Version + ")") && ExperimentalNotice.Version != "?",
				"the box says " + ExperimentalNotice.Header + ", the version (" + ExperimentalNotice.Version + ") and its three points");
			Canvas.ForceUpdateCanvases();
			Text head = panel.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Title");
			Check(ref ok, head != null && head.cachedTextGenerator.lineCount == 1, "its header on one line (" + (head != null ? head.cachedTextGenerator.lineCount + " line(s), size " + head.cachedTextGenerator.fontSizeUsedForBestFit : "no header") + ")");
			int w0 = Screen.width, h0 = Screen.height;
			FullScreenMode mode0 = Screen.fullScreenMode;
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			RectTransform buttons = canvas.transform.Find("MenuButtons") as RectTransform;
			try
			{
				foreach (Vector2Int size in ScreenSizes)
				{
					Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
					yield return new WaitForSecondsRealtime(1.2f);
					int w = Screen.width, h = Screen.height;
					if (box != null) { box.gameObject.SetActive(true); try { box.Close(); } catch { } box.Open(); }
					yield return new WaitForSecondsRealtime(0.8f);
					Canvas.ForceUpdateCanvases();
					Rect n = ScreenRect(panel);
					var why = new List<string>();
					if (n.xMin < -1f || n.yMin < -1f || n.xMax > w + 1f || n.yMax > h + 1f) why.Add("off the screen (" + n + ")");
					if (buttons != null && buttons.GetComponentsInChildren<Button>(false).Any(b => ScreenRect((RectTransform)b.transform).Overlaps(n))) why.Add("over Raft's menu buttons");
					// (the New Game box's drawn panel: its controls - its own rectangle is bigger than what it draws)
					if (box != null && box.GetComponentsInChildren<Selectable>(false).Any(s => ScreenRect((RectTransform)s.transform).Overlaps(n))) why.Add("over the New Game box");
					Check(ref ok, why.Count == 0, w + "x" + h + ": the box " + (why.Count == 0 ? "on the screen, clear of the menu and the New Game box" : string.Join(", ", why.ToArray())));
					if (size.x == 1024 || size.x == 1920) { Screenshot(new[] { "notice_" + w + "x" + h }); yield return new WaitForSecondsRealtime(0.6f); }
					if (box != null) box.gameObject.SetActive(false);
				}
			}
			finally { Screen.SetResolution(w0, h0, mode0); if (box != null) box.gameObject.SetActive(false); }
			yield return new WaitForSecondsRealtime(1f);
			// Got it / Show
			float tall = ScreenRect(panel).height;
			ExperimentalNotice.Toggle.onClick.Invoke(); yield return null; Canvas.ForceUpdateCanvases();
			Check(ref ok, ExperimentalNotice.Folded && ExperimentalNotice.SeenThisVersion && ScreenRect(panel).height < tall * 0.6f && UIKit.LabelOf(ExperimentalNotice.Toggle).text == "Show",
				"Got it folds it into a bar (" + tall.ToString("F0") + " > " + ScreenRect(panel).height.ToString("F0") + " px) and remembers it for " + ExperimentalNotice.Version);
			Screenshot(new[] { "notice_folded" });
			yield return new WaitForSecondsRealtime(0.6f);
			ExperimentalNotice.Toggle.onClick.Invoke(); yield return null;
			Check(ref ok, !ExperimentalNotice.Folded && !ExperimentalNotice.SeenThisVersion && UIKit.LabelOf(ExperimentalNotice.Toggle).text == "Got it", "Show opens it again (and a new start shows it whole)");
			// Its help: Discord, the guide, Report a problem, and moving it
			yield return NoticeHelpRoutine((c, w) => Check(ref ok, c, w));
			yield return NoticeCoverRoutine((c, w) => Check(ref ok, c, w));
			ExperimentalNotice.SavedPlace = placeBefore; // (where the player had dragged it)
			ExperimentalNotice.ApplySavedPlace();
			if (foldedBefore) ExperimentalNotice.Flip();
			if (ok) Log("PASS: experimental notice"); else Fail("experimental notice");
		}

		#endregion
		#region Every combination

		[ConsoleCommand(name: "CIOptionsMatrix", docs: "Dev, world (host): every combination (256) of the world options switched on in turn: each is the world's (the world file's lines, the host's message, CIServerSig's lines), what follows from it holds (the story order only with its option, the blueprints' pairs only with theirs, the storages' refusal only with theirs, ghost rafts only with theirs), no exceptions while the mod's ticks run a few seconds with it; the world's own options back after")]
		public static void OptionsMatrixCommand() { StartTest(OptionsMatrixRoutine()); }

		static IEnumerator OptionsMatrixRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("options matrix: host, in a world"); yield break; }
			bool ok = true;
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			var errors = new List<string>();
			Application.LogCallback watch = (text, trace, type) => { if (type == LogType.Exception || (type == LogType.Error && text.Contains("CUSTOM ISLANDS"))) errors.Add(text.Length > 160 ? text.Substring(0, 160) : text); };
			Application.logMessageReceived += watch;
			var bad = new List<string>();
			// (a storage noted for another player, one for nobody: the refusal only with the option)
			string storagesBefore = PrivateStorage.Encode();
			PrivateStorage.Decode(storagesBefore + ";999999:12345");
			try
			{
				for (int mask = 0; mask < (1 << WorldOptions.All.Length); mask++)
				{
					var on = new HashSet<string>(WorldOptions.All.Where((o, i) => (mask & (1 << i)) != 0));
					WorldOptions.Set(on);
					yield return new WaitForSeconds(2.5f);
					// (Raft's autosave prunes builders of storages not in the world - the made-up one too: noted again)
					if (PrivateStorage.BuilderOf(999999u) == 0UL) PrivateStorage.Decode(PrivateStorage.Encode() + ";999999:12345");
					string sig = string.Join("|", ServerSig().ToArray());
					var why = new List<string>();
					if (!WorldOptions.Current.SetEquals(on)) why.Add("options");
					if (!WorldOptions.WriteLines().Any(l => l == "@options=" + string.Join(",", WorldOptions.All.Where(on.Contains).ToArray()))) why.Add("world file line");
					IslandNetMessage m = WorldOptions.Message();
					if (m.Kind != IslandNetMessage.WorldOptions || m.Data != WorldOptions.Encode(on, WorldOptions.Seed)) why.Add("message");
					if (StoryOrder.Active != on.Contains(WorldOptions.StoryOrder) || (StoryOrder.Order.SequenceEqual(StoryOrder.Chain) == on.Contains(WorldOptions.StoryOrder))) why.Add("story order");
					if (ScrambledBlueprints.Movable.Count > 1 && ScrambledBlueprints.Active != on.Contains(WorldOptions.Blueprints)) why.Add("blueprints");
					if (!PrivateStorage.MayOpen(999998u, 1UL)) why.Add("a storage without a builder refused");
					if (PrivateStorage.MayOpen(999999u, 1UL) == on.Contains(WorldOptions.PrivateStorage)) why.Add("another's storage " + (on.Contains(WorldOptions.PrivateStorage) ? "opens" : "refused"));
					if (!sig.Contains("options " + WorldOptions.Encode(on, WorldOptions.Seed))) why.Add("CIServerSig");
					if (why.Count > 0) bad.Add(WorldOptions.Describe(on) + ": " + string.Join(", ", why.ToArray()));
				}
			}
			finally
			{
				Application.logMessageReceived -= watch;
				PrivateStorage.Decode(storagesBefore);
				WorldOptions.Set(optionsBefore);
			}
			Check(ref ok, bad.Count == 0, (1 << WorldOptions.All.Length) + " combinations: each the world's, and what follows from it holds" + (bad.Count > 0 ? " - not: " + string.Join("; ", bad.Take(4).ToArray()) : ""));
			Check(ref ok, errors.Count == 0, "no errors while they ran" + (errors.Count > 0 ? " - " + errors[0] : ""));
			if (ok) Log("PASS: options matrix"); else Fail("options matrix");
		}

		#endregion
	}
}
