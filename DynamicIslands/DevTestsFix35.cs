using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Audit 11-09 player bugs, round 3.5: item index clashes (#8), the reinforced storage's prefab (#11), one "-Latest"
	/// in a received world (#15), host-only messages from someone else (#14), hints with the bound keys (#12), and the island
	/// banner over the level-up box (#13).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIIndexClash", docs: "Dev, in game: every upgrade's live/declared index round-trips; an upgrade whose index another item holds moves to a free one (>= 29600), is listed as a clash, and the other item keeps its index")]
		public static void IndexClashCommand() { StartTest(IndexClashRoutine()); }

		static IEnumerator IndexClashRoutine()
		{
			bool ok = true;
			List<ExtraUpgrades.Upgrade> made = ExtraUpgrades.All.Concat(ExtraUpgrades.CosmeticItems).Where(u => u.Item != null).ToList();
			Check(ref ok, made.Count > 0, "upgrades made: " + made.Count);
			List<string> bad = made.Where(u => ExtraUpgrades.LiveIndex(u.Declared) != u.Index || ExtraUpgrades.DeclaredIndex(u.Index) != u.Declared
				|| u.Item.UniqueIndex != u.Index).Select(u => u.Name + " " + u.Declared + "/" + u.Index).ToList();
			Check(ref ok, bad.Count == 0, "live/declared index round trip" + (bad.Count > 0 ? " - WRONG " + string.Join(", ", bad.ToArray()) : ""));
			Check(ref ok, made.All(u => u.Index == u.Declared), "no other mod: every upgrade keeps its declared index (saves stay stable)");
			Check(ref ok, ExtraUpgrades.Clashed.Count == 0, "no clashes listed: " + ExtraUpgrades.Clashed.Count);
			Check(ref ok, ExtraUpgrades.LiveIndex(12345) == 12345 && ExtraUpgrades.DeclaredIndex(12345) == 12345, "an index not ours maps to itself");

			Item_Base rope = ItemManager.GetItemByName("Rope");
			if (rope == null) { Fail("index clash: Raft has no Rope"); yield break; }
			var test = new ExtraUpgrades.Upgrade { Index = rope.UniqueIndex, Name = "DI_TestClash", BaseName = "Rope", Display = "Test clash", Tint = Color.white, Description = "test" };
			bool madeIt = false;
			try { madeIt = ExtraUpgrades.Make(test); }
			catch (Exception e) { Check(ref ok, false, "Make threw: " + e.Message); }
			try
			{
				Check(ref ok, madeIt && test.Item != null, "an upgrade on a taken index is still made");
				Check(ref ok, test.Declared == rope.UniqueIndex && test.Index >= 29600 && test.Index != rope.UniqueIndex, "it moved: declared " + test.Declared + " -> " + test.Index);
				Item_Base atNew = ItemManager.GetItemByIndex(test.Index);
				Check(ref ok, atNew != null && atNew.UniqueName == "DI_TestClash", "its new index holds it: " + (atNew != null ? atNew.UniqueName : "null"));
				Item_Base atOld = ItemManager.GetItemByIndex(rope.UniqueIndex);
				Check(ref ok, atOld == rope, "the other item keeps its index: " + (atOld != null ? atOld.UniqueName : "null"));
				Check(ref ok, ExtraUpgrades.DeclaredIndex(test.Index) == rope.UniqueIndex, "its declared index is known (the upgrade table file keeps it)");
				Check(ref ok, ExtraUpgrades.Clashed.Contains("Test clash"), "listed as a clash (the one-time warning names it)");
				Check(ref ok, made.All(u => u.Index != test.Index), "the free index is no other upgrade's");
			}
			finally { ExtraUpgrades.ForgetForTest(test); }
			Check(ref ok, ItemManager.GetItemByName("DI_TestClash") == null && ExtraUpgrades.Clashed.Count == 0, "test item taken out again");
			yield return null;
			if (ok) Log("PASS: index clash"); else Fail("index clash");
		}

		[ConsoleCommand(name: "CIBigStoragePrefab", docs: "Dev, in game: the reinforced storage's block prefab already has the bigger inventory at registration (not only once a block wakes)")]
		public static void BigStoragePrefabCommand() { StartTest(BigStoragePrefabRoutine()); }

		static IEnumerator BigStoragePrefabRoutine()
		{
			bool ok = true;
			ExtraUpgrades.Upgrade u = ExtraUpgrades.Find("DI_ReinforcedStorage");
			if (u == null || u.Item == null || u.Base == null) { Fail("big storage prefab: no reinforced storage"); yield break; }
			Func<Item_Base, int> slots = it =>
			{
				Block[] p = it.settings_buildable != null ? it.settings_buildable.GetBlockPrefabs() : null;
				Storage_Small s = p != null ? p.Where(b => b != null).Select(b => b.GetComponentInChildren<Storage_Small>(true)).FirstOrDefault(x => x != null) : null;
				return s != null && s.inventoryPrefab != null ? s.inventoryPrefab.GetComponentsInChildren<Slot>(true).Length : -1;
			};
			int baseSlots = slots(u.Base), bigSlots = slots(u.Item);
			Check(ref ok, baseSlots > 0, "large storage prefab slots: " + baseSlots);
			Check(ref ok, bigSlots >= Mathf.CeilToInt(baseSlots * ExtraUpgrades.StorageSlotsFactor), "reinforced storage prefab slots at registration: " + bigSlots + " (want >= " + Mathf.CeilToInt(baseSlots * ExtraUpgrades.StorageSlotsFactor) + ")");
			yield return null;
			if (ok) Log("PASS: big storage prefab"); else Fail("big storage prefab");
		}

		[ConsoleCommand(name: "CIWorldShareLatest", docs: "Dev, anywhere: a received world folder never keeps two \"-Latest\" saves (own one moves to a free dated name, a received one goes), and a world the host renamed is moved, not doubled")]
		public static void WorldShareLatestCommand() { StartTest(WorldShareLatestRoutine()); }

		static IEnumerator WorldShareLatestRoutine()
		{
			bool ok = true;
			string root = Path.Combine(Path.GetTempPath(), "CI_WorldShareLatest");
			try
			{
				if (Directory.Exists(root)) Directory.Delete(root, true);
				string w = Path.Combine(root, "World");
				Func<string, bool, string> mk = (name, received) =>
				{
					string d = Path.Combine(w, name);
					Directory.CreateDirectory(d);
					if (received) File.WriteAllText(Path.Combine(d, WorldSaveShare.ReceivedFile), "1");
					return d;
				};
				// 1. the player's own -Latest, its dated name taken by their own backup: it takes the minute before
				mk("2026.11.10-12.00", false);
				WorldSaveShare.RetireLatest(mk("2026.11.10-12.00-Latest", false));
				Check(ref ok, !Directory.Exists(Path.Combine(w, "2026.11.10-12.00-Latest")) && Directory.Exists(Path.Combine(w, "2026.11.10-11.59")), "own -Latest with its name taken -> 2026.11.10-11.59");
				// 2. a received -Latest whose dated copy exists: it goes
				mk("2026.11.10-13.00", true);
				WorldSaveShare.RetireLatest(mk("2026.11.10-13.00-Latest", true));
				Check(ref ok, !Directory.Exists(Path.Combine(w, "2026.11.10-13.00-Latest")) && Directory.Exists(Path.Combine(w, "2026.11.10-13.00")), "received -Latest with a dated copy -> removed");
				// 3. the player's own -Latest, a received dated copy in the way: that copy is replaced
				string inWay = mk("2026.11.10-14.00", true);
				WorldSaveShare.RetireLatest(mk("2026.11.10-14.00-Latest", false));
				Check(ref ok, !Directory.Exists(inWay + "-Latest") && Directory.Exists(inWay) && !File.Exists(Path.Combine(inWay, WorldSaveShare.ReceivedFile)), "own -Latest over a received copy -> takes its name");
				// 4. no clash: a plain rename
				WorldSaveShare.RetireLatest(mk("2026.11.10-15.00-Latest", false));
				Check(ref ok, Directory.Exists(Path.Combine(w, "2026.11.10-15.00")), "own -Latest, name free -> dated");
				Check(ref ok, Directory.GetDirectories(w).Count(d => d.EndsWith("-Latest")) == 0, "no -Latest left");
				Check(ref ok, WorldSaveShare.FreeDatedName(Path.Combine(w, "not a date")).EndsWith("not a date (2)"), "an undated name gets (2)");

				// 5. the host renamed the world: the old folder (marker = this world) moves to the new name
				Guid g = Guid.NewGuid();
				string old = Path.Combine(root, "Old name");
				Directory.CreateDirectory(old);
				File.WriteAllLines(Path.Combine(old, WorldSaveShare.MarkerFile), new[] { g.ToString() });
				string got = WorldSaveShare.WorldFolderFor(root, "New name", g);
				Check(ref ok, got == Path.Combine(root, "New name") && Directory.Exists(got) && !Directory.Exists(old), "renamed world moved: " + Path.GetFileName(got));
				// 6. the new name is another world's: the old folder is used as it is
				Guid g2 = Guid.NewGuid();
				string other = Path.Combine(root, "Taken");
				Directory.CreateDirectory(other);
				File.WriteAllLines(Path.Combine(other, WorldSaveShare.MarkerFile), new[] { Guid.NewGuid().ToString() });
				string taken2 = Path.Combine(root, "Taken (" + g2.ToString().Substring(0, 8) + ")");
				Directory.CreateDirectory(taken2);
				File.WriteAllLines(Path.Combine(taken2, WorldSaveShare.MarkerFile), new[] { Guid.NewGuid().ToString() });
				string mine = Path.Combine(root, "Mine before");
				Directory.CreateDirectory(mine);
				File.WriteAllLines(Path.Combine(mine, WorldSaveShare.MarkerFile), new[] { g2.ToString() });
				got = WorldSaveShare.WorldFolderFor(root, "Taken", g2);
				Check(ref ok, got == mine, "new name taken by other worlds -> the old folder is kept: " + Path.GetFileName(got));
				// 7. a new world: its own name
				got = WorldSaveShare.WorldFolderFor(root, "Fresh", Guid.NewGuid());
				Check(ref ok, got == Path.Combine(root, "Fresh"), "a new world gets its name");
			}
			catch (Exception e) { Check(ref ok, false, "threw: " + e); }
			finally { try { Directory.Delete(root, true); } catch { } }
			yield return null;
			if (ok) Log("PASS: world share latest"); else Fail("world share latest");
		}

		[ConsoleCommand(name: "CIHostOnlyMsgs", docs: "Dev, anywhere: a player drops host-only mod messages that don't come from the host; the host and other kinds are untouched")]
		public static void HostOnlyMsgsCommand() { StartTest(HostOnlyMsgsRoutine()); }

		static IEnumerator HostOnlyMsgsRoutine()
		{
			bool ok = true;
			ulong was = IslandNetwork.HostIdOverride;
			IslandNetwork.HostIdOverride = 111UL;
			try
			{
				foreach (int kind in IslandNetwork.HostOnlyKinds)
				{
					var m = new IslandNetMessage { Kind = kind };
					if (!IslandNetwork.NotFromHost(m, 222UL, false)) Check(ref ok, false, "kind " + kind + " from a player not dropped");
					if (IslandNetwork.NotFromHost(m, 111UL, false)) Check(ref ok, false, "kind " + kind + " from the host dropped");
					if (IslandNetwork.NotFromHost(m, 222UL, true)) Check(ref ok, false, "kind " + kind + " dropped on the host");
				}
				Check(ref ok, IslandNetwork.HostOnlyKinds.Count >= 19, "host-only kinds checked: " + IslandNetwork.HostOnlyKinds.Count);
				foreach (int kind in new[] { IslandNetMessage.ObjectUsed, IslandNetMessage.QuestStep, IslandNetMessage.FileRequest, IslandNetMessage.FileChunk, IslandNetMessage.Claim, IslandNetMessage.Ping, IslandNetMessage.PlayerPlace })
					Check(ref ok, !IslandNetwork.NotFromHost(new IslandNetMessage { Kind = kind }, 222UL, false), "kind " + kind + " (players send it) passes");
				Check(ref ok, !IslandNetwork.NotFromHost(new IslandNetMessage { Kind = IslandNetMessage.Islands }, 0UL, false), "unknown sender: passes (never drops on missing data)");
				IslandNetwork.HostIdOverride = 0UL;
				if (ComponentManager<Raft_Network>.Value == null || Raft_Network.IsHost)
					Check(ref ok, !IslandNetwork.NotFromHost(new IslandNetMessage { Kind = IslandNetMessage.Islands }, 222UL, Raft_Network.IsHost), "no host id known / on the host: passes");
			}
			finally { IslandNetwork.HostIdOverride = was; }
			yield return null;
			if (ok) Log("PASS: host-only messages"); else Fail("host-only messages");
		}

		[ConsoleCommand(name: "CIHintKeys", docs: "Dev, in game: the notebook/journal/stats hints name the keys as bound now - Raft's notebook rebound shows the new key")]
		public static void HintKeysCommand() { StartTest(HintKeysRoutine()); }

		static IEnumerator HintKeysRoutine()
		{
			bool ok = true;
			Check(ref ok, ModKeys.JournalName == ModKeys.Name(ModKeys.Journal) && ModKeys.StatsName == ModKeys.Name(ModKeys.Stats), "journal/stats names: " + ModKeys.JournalName + " / " + ModKeys.StatsName);
			Keybind kb;
			if (MyInput.Keybinds == null || !MyInput.Keybinds.TryGetValue("NoteBookToggle", out kb) || kb == null)
				Check(ref ok, false, "Raft's NoteBookToggle keybind not found (keys: " + (MyInput.Keybinds != null ? string.Join(",", MyInput.Keybinds.Keys.ToArray()) : "none") + ")");
			else
			{
				KeyCode was = kb.MainKey;
				Check(ref ok, ModKeys.RaftNotebook == was, "notebook key as bound: " + ModKeys.RaftNotebookName);
				try
				{
					kb.MainKey = was == KeyCode.K ? KeyCode.L : KeyCode.K;
					Check(ref ok, ModKeys.RaftNotebook == kb.MainKey && ModKeys.RaftNotebookName == ModKeys.Name(kb.MainKey), "rebound notebook -> hint says " + ModKeys.RaftNotebookName);
				}
				finally { kb.MainKey = was; }
			}
			yield return null;
			if (ok) Log("PASS: hint keys"); else Fail("hint keys");
		}

		[ConsoleCommand(name: "CIBannerOverlap", docs: "Dev, in game: a long island banner and a level-up box shown together don't overlap; the box goes back up after")]
		public static void BannerOverlapCommand() { StartTest(BannerOverlapRoutine()); }

		static IEnumerator BannerOverlapRoutine()
		{
			bool ok = true;
			if (RAPI.GetLocalPlayer() == null) { Fail("banner overlap: needs a world"); yield break; }
			if (!PlayerLevels.On) { Fail("banner overlap: the level up system is off - run 'Levels on' first"); yield break; }
			IslandInfo.HideNow();
			LevelHud.Announce("Level up!", "Test level 99", true);
			yield return new WaitForSecondsRealtime(0.5f);
			Rect? alone = LevelHud.BannerScreenRect;
			foreach (int lines in new[] { 1, 4, 9 })
			{
				IslandInfo.HideNow();
				IslandInfo.Show("Overlap test island", "CI", string.Join(" ", Enumerable.Repeat("A long island description that wraps over several lines of the banner.", lines).ToArray()));
				LevelHud.Announce("Level up!", "Test level 99", true);
				yield return new WaitForSecondsRealtime(0.5f);
				Rect? isl = IslandInfo.BannerScreenRect, lvl = LevelHud.BannerScreenRect;
				Check(ref ok, isl.HasValue && lvl.HasValue, lines + " line(s): both up");
				if (isl.HasValue && lvl.HasValue)
					Check(ref ok, !isl.Value.Overlaps(lvl.Value) && lvl.Value.yMax <= isl.Value.yMin, lines + " line(s): island " + isl.Value.yMin.ToString("0") + ".." + isl.Value.yMax.ToString("0")
						+ ", level box " + lvl.Value.yMin.ToString("0") + ".." + lvl.Value.yMax.ToString("0"));
			}
			IslandInfo.HideNow();
			yield return new WaitForSecondsRealtime(0.3f);
			Rect? back = LevelHud.BannerScreenRect;
			if (alone.HasValue && back.HasValue) Check(ref ok, Mathf.Abs(back.Value.yMax - alone.Value.yMax) < 1f, "level box back in its place: " + back.Value.yMax.ToString("0") + " (was " + alone.Value.yMax.ToString("0") + ")");
			LevelHud.Clear();
			if (ok) Log("PASS: banner overlap"); else Fail("banner overlap");
		}

		[ConsoleCommand(name: "CIPlaceReal", docs: "Dev, world (host, a raft with foundations), RT18: the upgrade research table, a fast juicer and the golden shark trophy each held in the hotbar and placed by Raft's own BlockCreator.Update - its raycast from the camera (only the view turned to a raft surface), its ghost and build check (green), and a faked left click; the block is the upgrade, the hotbar item is used up; then taken down")]
		public static void PlaceRealCommand() { StartTest(PlaceRealRoutine()); }

		static IEnumerator PlaceRealRoutine()
		{
			bool[] ok = { true };
			var names = new[] { ExtraUpgrades.TableName, "DI_FastJuicer", Cosmetics.GoldenShark };
			yield return PlaceRealCore(names, "place real", ok, null);
			if (ok[0]) Log("PASS: place real"); else Fail("place real");
		}

		static Vector3? realAim;
		static int realClickFrame = -1;
		static bool realPatched;
		static Quaternion realCamWas;

		static void PatchPlaceReal()
		{
			if (realPatched) return;
			realPatched = true;
			var h = new HarmonyLib.Harmony("DynamicIslands.DevTests.PlaceReal");
			h.Patch(HarmonyLib.AccessTools.Method(typeof(BlockCreator), "GetQuadAtCursor"), prefix: new HarmonyLib.HarmonyMethod(typeof(DevTests), "RealAimPrefix"), postfix: new HarmonyLib.HarmonyMethod(typeof(DevTests), "RealAimPostfix"));
			h.Patch(HarmonyLib.AccessTools.Method(typeof(MyInput), "GetButtonDown", new[] { typeof(string) }), postfix: new HarmonyLib.HarmonyMethod(typeof(DevTests), "RealClickPostfix"));
		}

		// (the view only: the camera looks at the spot for Raft's own raycast, and is put back after it)
		static void RealAimPrefix(BlockCreator __instance)
		{
			if (!realAim.HasValue) return;
			Network_Player p = Traverse.Create(__instance).Field("playerNetwork").GetValue<Network_Player>();
			if (p == null || !p.IsLocalPlayer || p.CameraTransform == null) return;
			realCamWas = p.CameraTransform.rotation;
			p.CameraTransform.rotation = Quaternion.LookRotation(realAim.Value - p.CameraTransform.position);
		}
		static void RealAimPostfix(BlockCreator __instance)
		{
			if (!realAim.HasValue) return;
			Network_Player p = Traverse.Create(__instance).Field("playerNetwork").GetValue<Network_Player>();
			if (p != null && p.IsLocalPlayer && p.CameraTransform != null) p.CameraTransform.rotation = realCamWas;
		}
		static void RealClickPostfix(string identifier, ref bool __result)
		{
			if (!__result && identifier == "LMB" && Time.frameCount == realClickFrame) __result = true;
		}

		// (a floor takes most things; a trophy hangs on a side: the first face of the quad that takes the item)
		static Vector3? FaceFor(BlockQuad q, Item_Base item)
		{
			// (Raft's raycast hits the side that faces the camera: such a side first)
			Vector3 eye = Camera.main != null ? Camera.main.transform.position : q.transform.position + Vector3.up;
			Vector3 toEye = eye - q.transform.position;
			foreach (Vector3 d in new[] { q.transform.up, Vector3.up, q.transform.forward, -q.transform.forward, q.transform.right, -q.transform.right, -q.transform.up }.OrderByDescending(v => Vector3.Dot(v, toEye)))
				if (q.AcceptsBlock(item, d)) return d;
			return null;
		}

		static BlockQuad realSpot;

		// Aims Raft's own raycast at each raft surface that takes the item until Raft's build check says green: found[0] set, realSpot the quad;
		// found[1] the last answer. (a raft block's surfaces are switched off until a placeable is held: inactive ones count too)
		static IEnumerator GreenSpot(BlockCreator bc, Item_Base item, Network_Player player, string[] found, bool costFree = false)
		{
			realSpot = null; found[1] = "no surface";
			List<BlockQuad> quads = Resources.FindObjectsOfTypeAll<BlockQuad>().Where(q => q != null && q.gameObject.scene.IsValid() && q.quadType != null && FaceFor(q, item).HasValue)
				.OrderBy(q => (q.transform.position - player.transform.position).sqrMagnitude).Take(120).ToList();
			foreach (BlockQuad q in quads)
			{
				realAim = SurfacePoint(q, FaceFor(q, item).Value);
				CanvasHelper.ActiveMenu = MenuType.None;
				yield return null; yield return null; yield return null;
				Block ghost = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
				BlockQuad atCursor = Traverse.Create(bc).Field("quadAtCursor").GetValue<BlockQuad>();
				object err = ghost != null ? HarmonyLib.AccessTools.Method(typeof(BlockCreator), "CanBuildBlock").Invoke(bc, new object[] { ghost }) : null;
				found[1] = quads.Count + " surfaces, " + (atCursor != null ? "aimed" : "no quad at cursor (" + (realAim.Value - player.CameraTransform.position).magnitude.ToString("0.0") + " m, reach " + (Player.UseDistance * 2f).ToString("0.0") + ")") + ", " + (err != null ? err.ToString() : "no ghost");
				if (atCursor != null && err != null && (Convert.ToInt32(err) == 0 || (costFree && err.ToString() == "NoResources"))) { realSpot = q; found[0] = "yes"; yield break; }
			}
		}

		/// <summary>Each named item held and placed by Raft's own aim, ghost and click (RT18; CIPlaceOld for all of them).
		/// ok[0] false on any failed check; placedNames (if given) gets the items placed.</summary>
		static IEnumerator PlaceRealCore(string[] names, string label, bool[] okOut, List<string> placedNames)
		{
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost) { Check(ref ok, false, label + ": host, in a world"); okOut[0] = false; yield break; }
			try { PatchPlaceReal(); } catch (Exception e) { Check(ref ok, false, label + ": patch: " + e.Message); okOut[0] = false; yield break; }
			BlockCreator bc = player.BlockCreator;
			Hotbar hotbar = Traverse.Create(player.Inventory).Field("hotbar").GetValue<Hotbar>();
			var opts = new HashSet<string>(WorldOptions.Current);
			bool optionWas = opts.Contains(WorldOptions.Upgrades);
			if (!optionWas) { opts.Add(WorldOptions.Upgrades); WorldOptions.Set(opts); }
			MenuType menuWas = CanvasHelper.ActiveMenu;
			int placed = 0;
			Block wall = null;
			try
			{
				foreach (string name in names)
				{
					Item_Base item = ItemManager.GetItemByName(name);
					if (item == null) { Check(ref ok, false, name + ": no such item"); continue; }
					int had = player.Inventory.GetItemCount(name);
					// (in a hotbar slot, selected: Raft's PlayerItemManager hands it to the BlockCreator as when the player picks it)
					Slot into = player.Inventory.allSlots.Take(player.Inventory.hotslotCount > 0 ? player.Inventory.hotslotCount : 10).FirstOrDefault(sl => sl != null && sl.IsEmpty)
						?? player.Inventory.allSlots[0];
					into.SetItem(new ItemInstance(item, 1, item.MaxUses));
					// (the player's number key: Raft's hotbar takes the slot index, and placing uses up the item in that slot)
					int intoIndex = player.Inventory.allSlots.IndexOf(into);
					if (hotbar != null) { hotbar.SetSelectedSlotIndex(intoIndex); hotbar.ReselectCurrentSlot(); }
					yield return new WaitForSecondsRealtime(0.5f);
					Item_Base selected = Traverse.Create(bc).Field("selectedBuildableItem").GetValue<Item_Base>();
					Check(ref ok, selected == item, name + ": held, the build ghost is it (" + (selected != null ? selected.UniqueName : "none") + ")");
					// a raft surface that takes it: aim at each in turn until Raft's own check shows the ghost green
					string[] found = new string[2];
					yield return GreenSpot(bc, item, player, found);
					BlockQuad spot = found[0] != null ? realSpot : null;
					// (a trophy hangs on a wall, and a new raft has none: one wall put up first where Raft's ghost for it is green)
					if (spot == null && wall == null)
					{
						Item_Base wallItem = ItemManager.GetItemByName("Block_Wall_Wood");
						if (wallItem != null)
						{
							bc.SetBlockTypeToBuild(wallItem);
							string[] wf = new string[2];
							yield return GreenSpot(bc, wallItem, player, wf, true); // (the wall is put up by the cheat: its cost does not matter)
							Block wg = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
							if (wf[0] != null && wg != null)
							{
								DPS dps = Traverse.Create(bc).Field("quadSurface").Field("dpsType").GetValue<DPS>();
								try { wall = bc.CreateBlockCheat(wallItem, wg.transform.localPosition, wg.transform.localEulerAngles, dps, -1); } catch (Exception e) { Log("place real: wall for the trophy: " + e.Message); }
							}
							realAim = null;
							Log("place real: a wall for the trophy " + (wall != null ? "put up" : "not possible (" + wf[1] + ")"));
							if (hotbar != null) hotbar.ReselectCurrentSlot();
							yield return new WaitForSecondsRealtime(0.5f);
							if (wall != null) { found = new string[2]; yield return GreenSpot(bc, item, player, found); spot = found[0] != null ? realSpot : null; }
						}
					}
					string last = found[1]; int quadsCount = found[0] != null ? 1 : 0;
					if (spot == null) { Check(ref ok, false, name + ": no raft surface where the ghost is green (last: " + last + ")"); realAim = null; into.Reset(); continue; }
					{
						Block g0 = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
						BlockQuad q0 = Traverse.Create(bc).Field("quadAtCursor").GetValue<BlockQuad>();
						Check(ref ok, q0 != null && g0 != null && g0.gameObject.activeInHierarchy, name + ": aimed, a surface at the cursor (" + (q0 != null) + ") and the ghost shown (" + (g0 != null && g0.gameObject.activeInHierarchy) + ")");
					}
					HashSet<Block> before = new HashSet<Block>(BlockCreator.GetPlacedBlocks());
					CanvasHelper.ActiveMenu = MenuType.None;
					// (the click is held a few frames until Raft places it: one press, so at most one block)
					Block made = null;
					for (int f = 0; f < 10 && made == null; f++)
					{
						CanvasHelper.ActiveMenu = MenuType.None;
						realClickFrame = Time.frameCount + 1;
						yield return null;
						made = BlockCreator.GetPlacedBlocks().FirstOrDefault(b => b != null && !before.Contains(b));
					}
					realClickFrame = -1;
					realAim = null;
					yield return new WaitForSecondsRealtime(0.5f);
					if (made == null) made = BlockCreator.GetPlacedBlocks().FirstOrDefault(b => b != null && !before.Contains(b));
					UpgradeBlock ub = made != null ? made.GetComponent<UpgradeBlock>() : null;
					int now = player.Inventory.GetItemCount(name);
					Check(ref ok, made != null && made.buildableItem == item, name + ": placed by the click (" + (made != null ? made.buildableItem.UniqueName : "nothing") + ")");
					Check(ref ok, ub != null && ub.Upgrade != null, name + ": an upgrade block");
					Check(ref ok, now == had, name + ": the hotbar item was used up (" + had + " -> " + now + ", slot " + intoIndex + ", selected " + (hotbar != null ? hotbar.GetSelectedSlotIndex() : -1) + ")");
					if (made != null) { placed++; if (placedNames != null && made.buildableItem == item) placedNames.Add(name); BlockCreator.RemoveBlockNetwork(made, null, true); }
					yield return new WaitForSecondsRealtime(0.3f);
					int extra = player.Inventory.GetItemCount(name) - had;
					if (extra > 0) player.Inventory.RemoveItem(name, extra);
				}
			}
			finally
			{
				realAim = null; realClickFrame = -1;
				if (wall != null) BlockCreator.RemoveBlockNetwork(wall, null, true);
				CanvasHelper.ActiveMenu = menuWas;
				if (!optionWas) { opts.Remove(WorldOptions.Upgrades); WorldOptions.Set(opts); }
			}
			Log(label + ": " + placed + "/" + names.Length + " placed by Raft's own click");
			if (!ok) okOut[0] = false;
		}
	}
}
