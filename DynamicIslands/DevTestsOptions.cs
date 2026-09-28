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
		public static void MeasureBlueprintsCommand() { DynamicIslands.instance.StartCoroutine(MeasureBlueprintsRoutine()); }

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
		public static void WorldOptionsUnitCommand() { DynamicIslands.instance.StartCoroutine(WorldOptionsUnitRoutine()); }

		static IEnumerator WorldOptionsUnitRoutine()
		{
			yield return PlaceableCatalog.EnsureBuilt();
			bool ok = true;
			// The options' text
			HashSet<string> parsed = WorldOptions.Parse("storyorder, nonsense,GHOSTRAFTS;;privatestorage");
			Check(ref ok, parsed.Count == 3 && parsed.Contains(WorldOptions.StoryOrder) && parsed.Contains(WorldOptions.GhostRafts) && parsed.Contains(WorldOptions.PrivateStorage), "options read from text, unknown ones left out: " + WorldOptions.Describe(parsed));
			Check(ref ok, WorldOptions.Encode(WorldOptions.All, 42) == "on=blueprints,storyorder,ghostrafts,privatestorage;seed=42" && WorldOptions.Encode(new string[0], 7) == "on=;seed=7", "options written as text: " + WorldOptions.Encode(WorldOptions.All, 42));
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
				WorldOptions.SaveDefaults(new[] { WorldOptions.GhostRafts, WorldOptions.Blueprints });
				HashSet<string> d = WorldOptions.Defaults;
				Check(ref ok, d.Count == 2 && d.Contains(WorldOptions.GhostRafts) && d.Contains(WorldOptions.Blueprints) && WorldRules.ReadDefault("monsters") == (before != null ? WorldRules.ReadDefault("monsters") : null), "the last choice kept in world_rules.txt (" + WorldOptions.Describe(d) + "), the other lines untouched");
				WorldOptions.SaveDefaults(new string[0]);
				Check(ref ok, WorldOptions.Defaults.Count == 0, "... and none");
			}
			finally { if (before != null) File.WriteAllText(rules, before); else if (File.Exists(rules)) File.Delete(rules); }
			if (ok) Log("PASS: world options rules"); else Fail("world options rules");
		}

		#endregion

		#region The New Game box

		[ConsoleCommand(name: "CIWorldSettingsBox", docs: "Dev, main menu: Raft's New Game box's World settings clicked as a player does: the button opens the window over the box, each option's button switches it on and off (its label says so, the box's button counts them), All off, Done closes it and keeps the choice. Leaves the options named chosen for the next world: CIWorldSettingsBox [option ...] (default none)")]
		public static void WorldSettingsBoxCommand(string[] args) { DynamicIslands.instance.StartCoroutine(WorldSettingsBoxRoutine(WorldOptions.Parse(string.Join(",", args ?? new string[0])))); }

		static IEnumerator WorldSettingsBoxRoutine(HashSet<string> want)
		{
			NewGameBox box = Resources.FindObjectsOfTypeAll<NewGameBox>().FirstOrDefault(b => b.gameObject.scene.IsValid());
			if (box == null) { Fail("World settings box: no New Game box (main menu?)"); yield break; }
			bool ok = true;
			box.gameObject.SetActive(true);
			box.Open();
			yield return new WaitForSecondsRealtime(1f);
			Button open = WorldSettingsWindow.OpenButton;
			Check(ref ok, open != null && open.gameObject.activeInHierarchy, "the box has its World settings button ('" + (open != null ? UIKit.LabelOf(open).text : "") + "')");
			if (open == null) { Fail("World settings box"); yield break; }
			open.onClick.Invoke();
			yield return null;
			Check(ref ok, WorldSettingsWindow.IsOpen, "the button opens the window");
			Screenshot(new[] { "worldsettings" });
			yield return new WaitForSecondsRealtime(0.6f);
			var wrong = new List<string>();
			foreach (string o in WorldOptions.All)
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
			foreach (string o in WorldOptions.All) if (!WorldSettingsWindow.Chosen.Contains(o)) { WorldSettingsWindow.Toggle(o).onClick.Invoke(); yield return null; }
			Check(ref ok, WorldSettingsWindow.Chosen.Count == WorldOptions.All.Length && UIKit.LabelOf(open).text.Contains(WorldOptions.All.Length + " on"), "all on: the box's button says '" + UIKit.LabelOf(open).text + "'");
			Check(ref ok, ClickIn(WorldSettingsWindow.Window, "All off"), "All off clicked");
			yield return null;
			Check(ref ok, WorldSettingsWindow.Chosen.Count == 0 && UIKit.LabelOf(open).text.Contains("none"), "All off: none ('" + UIKit.LabelOf(open).text + "')");
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
		public static void GhostRaftsCommand(string[] args) { DynamicIslands.instance.StartCoroutine(GhostRaftsRoutine(args != null && args.Contains("keep"))); }

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

		[ConsoleCommand(name: "CIRaftDeckProbe", docs: "Dev, in game: Raft's own raft - for a few foundations: the block's height, the top of what a player stands on above it (Raft's raft collider), and the block's own colliders (their bounds) - to give the mod's rafts of blocks the same floor")]
		public static void RaftDeckProbeCommand()
		{
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			if (raft == null) { Fail("raft deck probe: no raft"); return; }
			foreach (Block b in raft.GetComponentsInChildren<Block>().Where(x => x.name.Contains("Foundation") || x.name.Contains("Floor")).Take(6))
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
			DynamicIslands.instance.StartCoroutine(StandAtRoutine(new Vector3(e.Position.x + x, h, e.Position.z + z)));
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
		public static void StoryOrderWorldCommand() { DynamicIslands.instance.StartCoroutine(StoryOrderWorldRoutine()); }

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
		[ConsoleCommand(name: "CIPlaceStorage", docs: "Dev, in game (host): places a small storage on the raft as a player builds one (through that player's BlockCreator, sent to every player): CIPlaceStorage [host|other] [height above the foundation] - logs STORAGE <index> by <player id>")]
		public static void PlaceStorageCommand(string[] args)
		{
			if (!Raft_Network.IsHost) { Fail("place storage: host only"); return; }
			Network_Player who = args != null && args.Length > 0 && args[0] == "other" ? OtherPlayer() : RAPI.GetLocalPlayer();
			float up = StorageHeight;
			if (args != null && args.Length > 1) float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out up);
			if (who == null) { Fail("place storage: no such player"); return; }
			Item_Base item = ItemManager.GetItemByName("Placeable_Storage_Small");
			Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
			List<Block> floors = raft != null ? raft.GetComponentsInChildren<Block>().Where(x => x.name.Contains("Foundation")).OrderBy(x => x.transform.localPosition.sqrMagnitude).ToList() : new List<Block>();
			// (the next foundation for each storage, round again on a small raft)
			Block floor = floors.Count > 0 ? floors[UnityEngine.Object.FindObjectsOfType<Storage_Small>().Length % floors.Count] : null;
			if (item == null || floor == null) { Fail("place storage: no storage item or no foundation"); return; }
			Block b = who.BlockCreator.CreateBlockCheat(item, floor.transform.localPosition + new Vector3(0f, up, 0f), Vector3.zero, DPS.Default, 0);
			if (b == null) { Fail("place storage: Raft didn't place it"); return; }
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

		[ConsoleCommand(name: "CIPrivateStorage", docs: "Dev, world (host, single player): the option Private storages alone - a storage built with the option on is the builder's (noted, saved in the world file's line); the builder opens it; one noted for another player (as if they built it) is refused for this player, looking at it says whose it is; with the option off both open; a storage built with it off has no builder; storages removed after")]
		public static void PrivateStorageCommand() { DynamicIslands.instance.StartCoroutine(PrivateStorageRoutine()); }

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
				Func<Block> place = () =>
				{
					int before = StorageManager.allStorages.Count;
					PlaceStorageCommand(new string[0]);
					Storage_Small s = StorageManager.allStorages.LastOrDefault();
					if (s != null && StorageManager.allStorages.Count > before) made.Add(s);
					return s;
				};
				Block mine = place();
				yield return null;
				Check(ref ok, mine != null && PrivateStorage.BuilderOf(mine.ObjectIndex) == me.steamID.Id, "a storage built with the option on is the builder's (" + (mine != null ? mine.ObjectIndex.ToString() : "none") + ")");
				Check(ref ok, WorldOptions.WriteLines().Any(l => l.StartsWith("@storages=") && mine != null && l.Contains(mine.ObjectIndex + ":" + me.steamID.Id)), "... kept in the world file's line");
				Storage_Small ms = mine as Storage_Small;
				bool opened = ms != null && me.StorageManager.OpenStorage(ms);
				if (opened) me.StorageManager.CloseStorage(ms);
				Check(ref ok, opened, "its builder opens it");
				Block theirs = place();
				yield return null;
				Storage_Small ts = theirs as Storage_Small;
				if (ts != null) PrivateStorage.Decode(PrivateStorage.Encode() + ";" + ts.ObjectIndex + ":12345");
				bool refused = ts != null && !me.StorageManager.OpenStorage(ts);
				Check(ref ok, refused && PrivateStorage.Refuses(ts, me) && PrivateStorage.LastRefusal.Contains("12345"), "one built by another player is refused for this one (" + PrivateStorage.LastRefusal + ")");
				WorldOptions.Set(new HashSet<string>(optionsBefore.Where(o => o != WorldOptions.PrivateStorage)));
				bool both = ms != null && ts != null && me.StorageManager.OpenStorage(ts);
				if (both) me.StorageManager.CloseStorage(ts);
				Check(ref ok, both, "the option off: it opens");
				Block free = place();
				yield return null;
				Check(ref ok, free != null && PrivateStorage.BuilderOf(free.ObjectIndex) == 0UL, "a storage built with the option off has no builder");
			}
			finally
			{
				foreach (Block b in made) if (b != null) UnityEngine.Object.Destroy(b.gameObject);
				PrivateStorage.Decode(storagesBefore);
				WorldOptions.Set(optionsBefore);
			}
			if (ok) Log("PASS: private storage"); else Fail("private storage");
		}

		#endregion

		#region Scrambled blueprints

		[ConsoleCommand(name: "CIBlueprintsWorld", docs: "Dev, world (host, a test world 'CI Options ...' only - it brings one of Raft's story islands): with the option on, the story island with the most movable blueprints is brought near the raft (Raft's own ChunkManager.AddChunkPointForcibly) and sailed to; each of its blueprint pickups gives its partner (item and name), none of what the story needs changes; the option off: Raft's own again, on: the partners again")]
		public static void BlueprintsWorldCommand() { DynamicIslands.instance.StartCoroutine(BlueprintsWorldRoutine()); }

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

		[ConsoleCommand(name: "CIExperimentalNotice", docs: "Dev, main menu: the EXPERIMENTAL RELEASE box - there with its header, the mod's version and its three points; at 8 screen sizes on the screen and clear of Raft's menu buttons and of the New Game box (opened); Got it folds it and remembers it for this version, Show opens it again and forgets it; its state before is put back; pictures shot_notice_*")]
		public static void ExperimentalNoticeCommand() { DynamicIslands.instance.StartCoroutine(ExperimentalNoticeRoutine()); }

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
					if (box != null) { box.gameObject.SetActive(true); box.Open(); }
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
			if (foldedBefore) ExperimentalNotice.Flip();
			if (ok) Log("PASS: experimental notice"); else Fail("experimental notice");
		}

		#endregion
		#region Every combination

		[ConsoleCommand(name: "CIOptionsMatrix", docs: "Dev, world (host): all 16 combinations of the world options switched on in turn: each is the world's (the world file's lines, the host's message, CIServerSig's lines), what follows from it holds (the story order only with its option, the blueprints' pairs only with theirs, the storages' refusal only with theirs, ghost rafts only with theirs), no exceptions while the mod's ticks run a few seconds with it; the world's own options back after")]
		public static void OptionsMatrixCommand() { DynamicIslands.instance.StartCoroutine(OptionsMatrixRoutine()); }

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
				for (int mask = 0; mask < 16; mask++)
				{
					var on = new HashSet<string>(WorldOptions.All.Where((o, i) => (mask & (1 << i)) != 0));
					WorldOptions.Set(on);
					yield return new WaitForSeconds(2.5f);
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
			Check(ref ok, bad.Count == 0, "16 combinations: each the world's, and what follows from it holds" + (bad.Count > 0 ? " - not: " + string.Join("; ", bad.Take(4).ToArray()) : ""));
			Check(ref ok, errors.Count == 0, "no errors while they ran" + (errors.Count > 0 ? " - " + errors[0] : ""));
			if (ok) Log("PASS: options matrix"); else Fail("options matrix");
		}

		#endregion
	}
}
