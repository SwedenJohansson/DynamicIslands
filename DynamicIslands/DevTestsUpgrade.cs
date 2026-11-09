using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIUpgradeHeal", docs: "Dev, in game (host, a test world 'CI ...'): a world saved by a build before 10-31 heals on load - a Receiver rule whose island is gone and a hand-spawned (SpawnIsland) copy is there adopts the copy (no new island); one with no island is brought again; a done story step is left; a quest stuck at 'open <the Vault>' with the Vault already open goes on (and stops at a chest not opened)")]
		public static void UpgradeHealCommand(string[] args) { StartTest(UpgradeHealRoutine()); }

		static IEnumerator UpgradeHealRoutine()
		{
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("only in a test world 'CI ...' (it changes the story chain for a moment)"); yield break; }
			yield return EnsureAlive();
			bool ok = true;

			// Part 1: the story chain's state as an old build left it (no frame in between: the chain's tick never sees it)
			const string nameA = "ciupgrade-a", nameB = "ciupgrade-b", nameC = "ciupgrade-c", nameD = "ciupgrade-d", nameE = "ciupgrade-e";
			IslandFile f = SampleIsland();
			f.Props[IslandQuest.KeyTitle] = "Upgrade";
			f.Props[IslandQuest.KeySteps] = "reach|cizone|1|\nread|CI note|1|";
			foreach (string n in new[] { nameA, nameB, nameC, nameD, nameE }) { f.Name = n; f.Save(IslandSpawner.PathFor(n)); }
			var rules = new List<IntroRule>
			{
				new IntroRule { Id = "ciupa", What = "island", WhatArg = nameA, When = "start", Where = "receiver" },          // gone, copies spawned by hand
				new IntroRule { Id = "ciupb", What = "island", WhatArg = nameB, When = "start", Where = "receiver" },          // gone, nothing left
				new IntroRule { Id = "ciupc", What = "island", WhatArg = nameC, When = "start", Where = "receiver", StoryPlace = "after:ciupx" }, // story step done
				new IntroRule { Id = "ciupd", What = "island", WhatArg = nameD, When = "start", Where = "receiver", StoryPlace = "after:ciupx" }, // story step open
				new IntroRule { Id = "ciupe", What = "island", WhatArg = nameE, When = "start", Where = "receiver" },          // fired, not tuned yet
			};
			var made = new List<IslandWorldState.Entry>();
			Vector3 far = raft.Value + new Vector3(40000f, 0f, 40000f);
			far.y = 0f;
			try
			{
				foreach (IntroRule r in rules) { StoryChain.Rules.Add(r); StoryChain.Fired.Add(r.Id); StoryChain.Brought.Add(r.Id); }
				StoryChain.Done.Add(StoryChain.RuleKey("ciupc"));
				StoryChain.Brought.Remove("ciupe");
				IslandWorldState.Entry e1 = IslandWorldState.Add(nameE, far + new Vector3(0f, 0f, 2000f), null, false); // (never visited)
				made.Add(e1);
				// (two copies of A spawned by hand, rule-less: the second one played to step 1)
				IslandWorldState.Entry a1 = IslandWorldState.Add(nameA, far, null, false), a2 = IslandWorldState.Add(nameA, far + new Vector3(2000f, 0f, 0f), null, false);
				made.Add(a1); made.Add(a2);
				a2.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				QuestTracker.Set(a2, 1, 0, false);
				int before = IslandWorldState.Islands.Count;

				StoryUpgrade.Run();
				Log("  repair: " + string.Join("; ", StoryUpgrade.Report.Where(l => l.Contains("ciup")).ToArray()));
				Check(ref ok, a2.Rule == "ciupa" && a1.Rule == "", "the copy played furthest is the rule's island now (" + (a1.Rule == "" ? "-" : a1.Rule) + " / " + (a2.Rule == "" ? "-" : a2.Rule) + ")");
				Check(ref ok, StoryChain.Brought.Contains("ciupa") && QuestTracker.StepOf(a2) == 1, "it stays brought, its quest as played (step " + QuestTracker.StepOf(a2) + ")");
				Check(ref ok, IslandWorldState.Islands.Count == before, "no island was added (" + before + " -> " + IslandWorldState.Islands.Count + ")");
				Check(ref ok, !StoryChain.Brought.Contains("ciupb") && StoryChain.Fired.Contains("ciupb"), "a Receiver rule with no island left is brought again when tuned (not brought, still fired)");
				Check(ref ok, StoryChain.Brought.Contains("ciupc"), "a done story step whose island is gone is left as it is (nothing comes twice)");
				Check(ref ok, !StoryChain.Brought.Contains("ciupd"), "an open story step whose island is gone is brought again");
				Check(ref ok, e1.Rule == "" && !StoryChain.Brought.Contains("ciupe"), "a rule not tuned yet leaves an unvisited copy alone (it comes as usual)");
				Check(ref ok, WorldDirector.Refs("ciupa", null).Count == 1 && WorldDirector.Refs("ciupa", null)[0] == a2, "the rule finds its island (the journal and notebook read it)");
				StoryUpgrade.Run();
				Check(ref ok, !StoryUpgrade.Report.Any(l => l.Contains("ciup")), "run again it finds nothing to do");
			}
			finally
			{
				foreach (IntroRule r in rules)
				{
					StoryChain.Rules.Remove(r); StoryChain.Fired.Remove(r.Id); StoryChain.Brought.Remove(r.Id); StoryChain.Done.Remove(StoryChain.RuleKey(r.Id));
				}
				if (made.Count > 0) IslandWorldState.RemoveIds(made.Select(e => e.Id).ToArray(), false);
				foreach (string n in new[] { nameA, nameB, nameC, nameD, nameE }) try { System.IO.File.Delete(IslandSpawner.PathFor(n)); } catch { }
			}

			// Part 2: a quest stuck at "open the Vault" with the Vault open already (an old build gave it to an "any chest" step)
			IslandFile v = PlayerTestIsland((file, add) =>
			{
				add("Loot_Chest", 6f, 0f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Vault" }, { ObjectProps.NoteText, "The vault." } });
				add("Loot_Chest", -6f, 0f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Box" }, { ObjectProps.NoteText, "A box." } });
				add("Loot_Chest", 0f, 6f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Crate" }, { ObjectProps.NoteText, "A crate." } });
				add("Loot_Chest", 0f, -6f, new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*1" }, { ObjectProps.NoteTitle, "Bell" }, { ObjectProps.NoteText, "A bell chest." } });
				file.Props[IslandQuest.KeyTitle] = "Vault order";
				file.Props[IslandQuest.KeySteps] = "open||1|Open any chest\nopen|Vault|1|Open the vault\nopen|Box|1|Open the box\nopen|Bell|2|Open the bell chest twice";
			});
			IslandWorldState.Entry e2 = null;
			yield return SpawnPlayerIsland(v, x => e2 = x);
			try
			{
				for (float t = 0f; t < 20f && (e2 == null || e2.Root == null); t += 0.5f) yield return new WaitForSeconds(0.5f);
				if (e2 == null || e2.Root == null) { Fail("the vault island did not come"); yield break; }
				Func<string, LootCrate> crate = title => e2.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault(c => c.GetComponent<CustomNote>() != null && c.GetComponent<CustomNote>().Title == title);
				LootCrate vault = crate("Vault"), box = crate("Box");
				if (vault == null || box == null || crate("Crate") == null) { Fail("the chests aren't there with their titles"); yield break; }
				Check(ref ok, QuestTracker.StepOf(e2) == 0, "the quest starts at 'open any chest' (step " + QuestTracker.StepOf(e2) + ")");
				// (the old save: the Vault opened already, its loot had)
				e2.State[vault.StateKey] = new ObjectState { Active = false, Yield = 0, Day = 0 };
				QuestTracker.Event(e2, "open", "Crate");
				Check(ref ok, QuestTracker.StepOf(e2) == 2, "another chest opened: the step 'open the Vault', its chest open already, goes on at once - and it waits at the box not opened (step " + QuestTracker.StepOf(e2) + " of 3)");
				QuestTracker.HealSpentChests(e2);
				Check(ref ok, QuestTracker.StepOf(e2) == 2, "on load again: still at the box (step " + QuestTracker.StepOf(e2) + ")");
				// (a save stuck at the box with it open: loading the island heals it)
				e2.State[box.StateKey] = new ObjectState { Active = false, Yield = 0, Day = 0 };
				QuestTracker.HealSpentChests(e2);
				Check(ref ok, QuestTracker.StepOf(e2) == 3, "loaded with the box open already: it goes on to the bell chest (step " + QuestTracker.StepOf(e2) + " of 4)");
				// RK7: one refilling chest, a step that wants it opened twice - open already counts once, not the whole step
				LootCrate bell = crate("Bell");
				e2.State[bell.StateKey] = new ObjectState { Active = false, Yield = 0, Day = 0 };
				QuestTracker.HealSpentChests(e2);
				QuestTracker.HealSpentChests(e2);
				Check(ref ok, QuestTracker.StepOf(e2) == 3 && QuestTracker.ProgressOf(e2, 0) == 1, "the one bell chest open already: 1 of 2 counted, once however often it loads (step " + QuestTracker.StepOf(e2) + ", " + QuestTracker.ProgressOf(e2, 0) + " of 2)");
			}
			finally { RemovePlayerIsland(e2); }
			if (ok) Log("PASS: upgrade heal"); else Fail("upgrade heal");
		}

		[ConsoleCommand(name: "CIUpgradePlace", docs: "Dev, world (host, a raft with foundations): every buildable upgrade and buildable cosmetic placed the way a player places it, with the world option Extra upgrades on and off: every surface type (SO_BlockQuadType) and overlap mask (SO_BlockCollisionMask) that takes its base item takes it too; on a surface of the raft that takes the base item, the build ghost (BlockCreator.SetBlockTypeToBuild) gets the same build check result (CanBuildBlock) as the base item's ghost there; then placed there, it is an upgrade block (tinted, its stats set) and is taken down again (`keep`: the ones placed with the option off stay, for `CIUpgradePlace loaded` after CISave + CILoadWorld). The option put back")]
		public static void UpgradePlaceCommand(string[] args)
		{
			string part = args != null && args.Length > 0 ? args[0] : "";
			if (part == "loaded") StartTest(UpgradePlaceLoadedRoutine()); else StartTest(UpgradePlaceRoutine(part == "keep"));
		}

		/// <summary>The upgrades `CIUpgradePlace keep` left on the raft (statics outlive a world reload).</summary>
		static readonly List<string> upgradePlaceKept = new List<string>();

		/// <summary>`CIUpgradePlace loaded`, after CISave and CILoadWorld: every upgrade kept is on the raft again as an upgrade
		/// block (tinted, its stats set); then all of them are taken down.</summary>
		static IEnumerator UpgradePlaceLoadedRoutine()
		{
			yield return new WaitForSeconds(1f);
			bool ok = upgradePlaceKept.Count > 0;
			List<Block> blocks = BlockCreator.GetPlacedBlocks().Where(b => b != null && b.buildableItem != null && ExtraUpgrades.FindMade(b.buildableItem) != null).ToList();
			foreach (string name in upgradePlaceKept)
			{
				Block b = blocks.FirstOrDefault(x => x.buildableItem.UniqueName == name);
				UpgradeBlock ub = b != null ? b.GetComponent<UpgradeBlock>() : null;
				Check(ref ok, ub != null && ub.Upgrade != null && ub.Upgrade.Name == name, "loaded: " + name + " on the raft as an upgrade block");
			}
			Log("upgrade place loaded: " + blocks.Count + " upgrade blocks found, " + upgradePlaceKept.Count + " kept");
			foreach (Block b in blocks) BlockCreator.RemoveBlockNetwork(b, null, true);
			upgradePlaceKept.Clear();
			if (ok) Log("PASS: upgrade place loaded"); else Fail("upgrade place loaded");
		}

		static IEnumerator UpgradePlaceRoutine(bool keep)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost || !CustomIslandSpawner.RaftPosition.HasValue) { Fail("upgrade place: host, in a world"); yield break; }
			BlockCreator bc = player.BlockCreator;
			var setType = HarmonyLib.AccessTools.Method(typeof(BlockCreator), "SetBlockTypeToBuild", new[] { typeof(Item_Base) });
			var canBuild = HarmonyLib.AccessTools.Method(typeof(BlockCreator), "CanBuildBlock");
			var destroyGhost = HarmonyLib.AccessTools.Method(typeof(BlockCreator), "DestroyGhostBlock");
			Transform pivot = HarmonyLib.Traverse.Create(typeof(BlockCreator)).Field("lockedBuildPivot").GetValue<Transform>();
			if (setType == null || canBuild == null || destroyGhost == null || pivot == null) { Fail("upgrade place: Raft's BlockCreator methods not found"); yield break; }

			List<ExtraUpgrades.Upgrade> ups = ExtraUpgrades.All.Concat(ExtraUpgrades.CosmeticItems)
				.Where(u => u.Item != null && u.Base != null && u.Item.settings_buildable != null && u.Item.settings_buildable.Placeable && u.Item.settings_buildable.GetBlockPrefabs() != null && u.Item.settings_buildable.GetBlockPrefabs().Length > 0).ToList();
			var quadTypes = Resources.FindObjectsOfTypeAll<SO_BlockQuadType>();
			var masks = Resources.FindObjectsOfTypeAll<SO_BlockCollisionMask>();
			var optionsBefore = new HashSet<string>(WorldOptions.Current);
			bool ok = ups.Count > 0;
			Log("upgrade place: " + ups.Count + " buildable upgrades, " + quadTypes.Length + " surface types, " + masks.Length + " overlap masks");
			if (keep) upgradePlaceKept.Clear();
			try
			{
				foreach (bool optionOn in new[] { true, false })
				{
					var opts = new HashSet<string>(optionsBefore);
					if (optionOn) opts.Add(WorldOptions.Upgrades); else opts.Remove(WorldOptions.Upgrades);
					WorldOptions.Set(opts);
					yield return null; yield return null;
					string tag = "option " + (optionOn ? "on" : "off");
					foreach (ExtraUpgrades.Upgrade u in ups)
					{
						int baseTypes = quadTypes.Count(q => q.AcceptsBlock(u.Base)), upTypes = quadTypes.Count(q => q.AcceptsBlock(u.Base) && q.AcceptsBlock(u.Item));
						int baseMasks = masks.Count(m => m.IgnoresBlock(u.Base)), upMasks = masks.Count(m => m.IgnoresBlock(u.Base) && m.IgnoresBlock(u.Item));
						Check(ref ok, baseTypes > 0 && upTypes == baseTypes && upMasks == baseMasks, tag + ", " + u.Display + ": surfaces " + upTypes + "/" + baseTypes + ", overlap masks " + upMasks + "/" + baseMasks);

						// a surface of the raft that takes the base item, where its ghost passes Raft's build check
						BlockQuad spot = null; Vector3 normal = Vector3.up; object baseErr = null, upErr = null; Vector3 at = Vector3.zero;
						var tried = new Dictionary<string, int>();
						// (the cause, 11-09: Raft's own lists don't name the upgrade item - the patch answers for it)
						int rawLists = quadTypes.Count(q => { var l = HarmonyLib.Traverse.Create(q).Field("acceptableBlockTypes").GetValue<List<Item_Base>>(); return l != null && l.Contains(u.Item); });
						if (rawLists > 0) Log("upgrade place: " + u.Display + " is named in " + rawLists + " of Raft's surface lists itself");
						// (a raft block's surfaces are switched off until a hammer or placeable is held: found inactive too)
						List<BlockQuad> all = Resources.FindObjectsOfTypeAll<BlockQuad>().Where(q => q != null && q.gameObject.scene.IsValid()).ToList();
						tried["placed blocks"] = BlockCreator.GetPlacedBlocks().Count;
						tried["scene quads"] = all.Count;
						tried["with type"] = all.Count(q => q.quadType != null);
						List<BlockQuad> quads = all.Where(q => q.quadType != null).OrderBy(q => (q.transform.position - player.transform.position).sqrMagnitude).Take(120).ToList();
						tried["surfaces"] = quads.Count;
						foreach (BlockQuad q in quads)
						{
							Vector3 n = new[] { q.transform.up, Vector3.up, q.transform.forward, -q.transform.forward, q.transform.right, -q.transform.right, -q.transform.up }.FirstOrDefault(d => q.AcceptsBlock(u.Base, d));
							if (n == Vector3.zero) continue;
							Vector3 p = SurfacePoint(q, n);
							object e = GhostCheck(bc, setType, canBuild, destroyGhost, u.Base, p);
							string k = e == null ? "no ghost" : e.ToString(); int c; tried.TryGetValue(k, out c); tried[k] = c + 1;
							if (e == null) continue;
							// (the first surface that takes the base item is kept even if Raft's build check says no there - a fresh
							//  raft is small: the check is then compared base against upgrade on the same spot)
							if (spot == null || (Convert.ToInt32(e) == 0 && Convert.ToInt32(baseErr) != 0)) { spot = q; normal = n; baseErr = e; at = p; }
							if (Convert.ToInt32(e) == 0) break;
						}
						if (spot == null) { Log("upgrade place: " + u.Display + ": skipped, no surface of a fresh raft takes " + u.Base.UniqueName + " (surface and overlap lists checked above; search " + string.Join(", ", tried.Select(kv => kv.Key + " x" + kv.Value).ToArray()) + ")"); continue; }
						bool accepts = spot.AcceptsBlock(u.Item, normal);
						upErr = GhostCheck(bc, setType, canBuild, destroyGhost, u.Item, at);
						Check(ref ok, accepts && upErr != null && upErr.ToString() == baseErr.ToString(), tag + ", " + u.Display + ": the raft surface takes it (" + accepts + "), build check " + upErr + " (base " + baseErr + ")");
						Block b = bc.CreateBlockCheat(u.Item, pivot.InverseTransformPoint(at), Vector3.zero, DPS.Default, 0);
						yield return null;
						UpgradeBlock ub = b != null ? b.GetComponent<UpgradeBlock>() : null;
						Check(ref ok, b != null && b.buildableItem == u.Item && ub != null && ub.Upgrade == u, tag + ", " + u.Display + ": placed, an upgrade block (" + (ub != null && ub.Upgrade != null) + ")");
						if (b != null && keep && !optionOn) upgradePlaceKept.Add(u.Name);
						else if (b != null) BlockCreator.RemoveBlockNetwork(b, null, true);
						yield return null;
					}
				}
			}
			finally
			{
				WorldOptions.Set(optionsBefore);
				try { destroyGhost.Invoke(bc, null); } catch { }
			}
			if (ok) Log("PASS: upgrade place"); else Fail("upgrade place");
		}

		/// <summary>The build ghost of an item at a spot and Raft's build check on it (a BuildError), or null.</summary>
		// (the middle of the quad's face that looks along n: where Raft's own aim puts a placeable)
		static Vector3 SurfacePoint(BlockQuad q, Vector3 n)
		{
			BoxCollider col = q.GetComponent<BoxCollider>();
			if (col == null) return q.transform.position;
			Bounds b = col.bounds;
			return b.center + n.normalized * Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z)).normalized));
		}

		static object GhostCheck(BlockCreator bc, System.Reflection.MethodInfo setType, System.Reflection.MethodInfo canBuild, System.Reflection.MethodInfo destroyGhost, Item_Base item, Vector3 at)
		{
			try
			{
				setType.Invoke(bc, new object[] { item });
				Block ghost = HarmonyLib.Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
				if (ghost == null) return null;
				ghost.transform.position = at;
				Physics.SyncTransforms();
				return canBuild.Invoke(bc, new object[] { ghost });
			}
			catch (Exception e) { Log("upgrade place: ghost of " + item.UniqueName + ": " + (e.InnerException ?? e).Message); return null; }
			finally { try { destroyGhost.Invoke(bc, null); } catch { } }
		}
	}
}
