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
	/// <summary>Player report 11-12: a crafted upgrade held in the hotbar showed no build ghost. Every item the mod adds that
	/// Raft can build (the upgrades and the placeable cosmetic rewards) is held like a player holds it and aimed at raft
	/// surfaces by Raft's own BlockCreator.Update; its ghost must show there, green, where it will stand, drawn in Raft's
	/// ghost colour, and the player's hand must be the one Raft shows for its base item.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIPlaceGhosts", docs: "Dev, world (host, a raft with foundations), GH1: every placeable item the mod adds (upgrades, cosmetic rewards) held in the hotbar: Raft's own aim finds a green spot, the ghost is shown there in Raft's ghost colour at the spot, and the hand model is the base item's. CIPlaceGhosts [name...] = only those; 'shot' in the list also saves ghost_<item>.jpg from the player's view")]
		public static void PlaceGhostsCommand(string[] args) { StartTest(PlaceGhostsRoutine(args ?? new string[0])); }

		/// <summary>A ghost's look: how many of its drawn renderers carry Raft's ghost colour, its bounds, and where it stands.</summary>
		static string GhostLook(Block ghost, out int drawn, out int ghostly, out Bounds bounds)
		{
			drawn = 0; ghostly = 0; bounds = new Bounds(ghost.transform.position, Vector3.zero);
			GameManager gm = SingletonGeneric<GameManager>.Singleton;
			bool any = false;
			var odd = new List<string>();
			// (only the renderers Raft itself colours, OccupyingComponent.renderers: a lamp's flame billboard stays as it is, in Raft's own lamp too)
			var raftColours = new HashSet<Renderer>(ghost.occupyingComponent != null && ghost.occupyingComponent.renderers != null ? ghost.occupyingComponent.renderers : new Renderer[0]);
			foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>(false))
			{
				if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || !raftColours.Contains(r)) continue;
				drawn++;
				if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
				bool g = false;
				var mats = r.sharedMaterials;
				var pb = new MaterialPropertyBlock();
				for (int i = 0; i < mats.Length; i++)
				{
					Material m = mats[i];
					if (m == null) continue;
					if (gm != null && (m == gm.ghostMaterialGreen || m == gm.ghostMaterialRed)) g = true;
					else if (gm != null && m.shader == gm.blockPaintShader) { r.GetPropertyBlock(pb, i); if (!pb.isEmpty) g = true; }
				}
				if (g) ghostly++; else if (odd.Count < 4) odd.Add(r.name + "[" + string.Join(",", mats.Where(m => m != null).Select(m => m.name + "/" + m.shader.name).ToArray()) + "]");
			}
			return odd.Count > 0 ? " not ghost-coloured: " + string.Join(" ", odd.ToArray()) : "";
		}

		static string HeldName(Network_Player player)
		{
			UseItemController c = player.PlayerItemManager != null ? player.PlayerItemManager.useItemController : null;
			ItemConnection a = c != null ? Traverse.Create(c).Field("activeObject").GetValue<ItemConnection>() : null;
			if (a == null || a.obj == null) return "(nothing)";
			return a.obj.name + (a.obj.activeInHierarchy ? "" : " (hidden)");
		}

		static IEnumerator PlaceGhostsRoutine(string[] args)
		{
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost) { Fail("place ghosts: host, in a world"); yield break; }
			try { PatchPlaceReal(); } catch (Exception e) { Fail("place ghosts: patch: " + e.Message); yield break; }
			bool shots = args.Any(a => a.Equals("shot", StringComparison.OrdinalIgnoreCase));
			var only = new HashSet<string>(args.Where(a => !a.Equals("shot", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
			BlockCreator bc = player.BlockCreator;
			Hotbar hotbar = Traverse.Create(player.Inventory).Field("hotbar").GetValue<Hotbar>();
			UseItemController uic = player.PlayerItemManager.useItemController;
			var opts = new HashSet<string>(WorldOptions.Current);
			bool optionWas = opts.Contains(WorldOptions.Upgrades);
			if (!optionWas) { opts.Add(WorldOptions.Upgrades); WorldOptions.Set(opts); }
			MenuType menuWas = CanvasHelper.ActiveMenu;
			// every item the mod adds that Raft builds (its index is the mod's: ExtraUpgrades makes all of them)
			List<Item_Base> items = ItemManager.GetAllItems().Where(i => i != null && ExtraUpgrades.FindMade(i) != null && i.settings_buildable != null && i.settings_buildable.HasBuildablePrefabs)
				.Where(i => only.Count == 0 || only.Contains(i.UniqueName)).OrderBy(i => i.UniqueIndex).ToList();
			int good = 0;
			var bad = new List<string>();
			Block wall = null;
			Slot into = null;
			var grown = new List<Block>();
			try
			{
				// (a new world's raft is 2 x 2: too small for an engine, a bed or a turbine next to the rest; grown to 6 x 6, taken off after)
				Item_Base fItem = ItemManager.GetItemByName("Block_Foundation");
				List<Block> fs = UnityEngine.Object.FindObjectsOfType<Block>().Where(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName == "Block_Foundation").ToList();
				Log("place ghosts: " + fs.Count + " foundations on the raft, " + BlockCreator.GetPlacedBlocks().Count + " blocks (" + string.Join(", ", BlockCreator.GetPlacedBlocks().Where(b => b != null && b.buildableItem != null).Select(b => b.buildableItem.UniqueName).Distinct().Take(6).ToArray()) + ")");
				if (fItem != null && fs.Count > 0)
				{
					Vector3 o = fs[0].transform.localPosition;
					const float g = 1.5f;
					for (int x = -2; x <= 3; x++)
						for (int z = -2; z <= 3; z++)
						{
							Vector3 p = o + new Vector3(x * g, 0f, z * g);
							if (fs.Any(b => (b.transform.localPosition - p).sqrMagnitude < 0.04f)) continue;
							try { Block made = bc.CreateBlockCheat(fItem, p, fs[0].transform.localEulerAngles, DPS.Default, -1); if (made != null) grown.Add(made); }
							catch (Exception e) { Log("place ghosts: a foundation: " + e.Message); break; }
						}
					yield return new WaitForSecondsRealtime(0.5f);
					Log("place ghosts: the raft grown by " + grown.Count + " foundations");
				}
				foreach (Item_Base item in items)
				{
					ExtraUpgrades.Upgrade u = ExtraUpgrades.FindMade(item);
					string name = item.UniqueName;
					bool itemOk = true;
					into = player.Inventory.allSlots.Take(player.Inventory.hotslotCount > 0 ? player.Inventory.hotslotCount : 10).FirstOrDefault(sl => sl != null && sl.IsEmpty) ?? player.Inventory.allSlots[0];
					// (the base item first, held the same way: the hand Raft shows for it)
					string baseHand = null;
					if (u.Base != null)
					{
						into.SetItem(new ItemInstance(u.Base, 1, u.Base.MaxUses));
						if (hotbar != null) { hotbar.SetSelectedSlotIndex(player.Inventory.allSlots.IndexOf(into)); hotbar.ReselectCurrentSlot(); }
						yield return new WaitForSecondsRealtime(0.4f);
						baseHand = HeldName(player);
					}
					into.SetItem(new ItemInstance(item, 1, item.MaxUses));
					if (hotbar != null) { hotbar.SetSelectedSlotIndex(player.Inventory.allSlots.IndexOf(into)); hotbar.ReselectCurrentSlot(); }
					yield return new WaitForSecondsRealtime(0.5f);
					string hand = HeldName(player);
					Item_Base selected = Traverse.Create(bc).Field("selectedBuildableItem").GetValue<Item_Base>();
					Check(ref itemOk, selected == item, name + ": held, the build ghost is it (" + (selected != null ? selected.UniqueName : "none") + ")");
					// the hand: Raft's for the base item (a placeable's "Placeable" hand) or the upgrade's own copy of it
					bool handOk = baseHand == null || hand == baseHand || hand.StartsWith(baseHand.Replace(" (hidden)", "") + "_");
					Check(ref itemOk, handOk, name + ": hand model " + hand + " (base item's: " + (baseHand ?? "?") + ")");
					string[] found = new string[2];
					yield return GreenSpot(bc, item, player, found);
					BlockQuad spot = found[0] != null ? realSpot : null;
					if (spot == null && wall == null)
					{
						Item_Base wallItem = ItemManager.GetItemByName("Block_Wall_Wood");
						if (wallItem != null)
						{
							bc.SetBlockTypeToBuild(wallItem);
							string[] wf = new string[2];
							yield return GreenSpot(bc, wallItem, player, wf, true);
							Block wg = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
							if (wf[0] != null && wg != null)
							{
								DPS dps = Traverse.Create(bc).Field("quadSurface").Field("dpsType").GetValue<DPS>();
								try { wall = bc.CreateBlockCheat(wallItem, wg.transform.localPosition, wg.transform.localEulerAngles, dps, -1); } catch (Exception e) { Log("place ghosts: a wall: " + e.Message); }
							}
							realAim = null;
							if (hotbar != null) hotbar.ReselectCurrentSlot();
							yield return new WaitForSecondsRealtime(0.5f);
							if (wall != null) { found = new string[2]; yield return GreenSpot(bc, item, player, found); spot = found[0] != null ? realSpot : null; }
						}
					}
					if (spot == null) Check(ref itemOk, false, name + ": no raft surface where the ghost is green (last: " + found[1] + ")");
					else
					{
						// (aimed at the spot a few frames: the ghost as the player sees it)
						// (10 frames: a ghost that blinks off or is made again every frame shows here)
						int shown = 0, remade = 0;
						var errs = new HashSet<string>();
						Block first = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
						for (int f = 0; f < 10; f++)
						{
							yield return null;
							Block gNow = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
							if (gNow != first) { remade++; first = gNow; }
							if (gNow != null && gNow.gameObject.activeInHierarchy) shown++;
							object e2 = gNow != null ? HarmonyLib.AccessTools.Method(typeof(BlockCreator), "CanBuildBlock").Invoke(bc, new object[] { gNow }) : null;
							errs.Add((Traverse.Create(bc).Field("quadAtCursor").GetValue<BlockQuad>() != null ? "quad " : "no quad ") + (e2 != null ? e2.ToString() : "no ghost"));
						}
						Log("place ghosts: " + name + ": shown " + shown + "/10 frames, made again " + remade + "x, " + string.Join("; ", errs.ToArray()));
						Block ghost = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
						if (ghost == null) Check(ref itemOk, false, name + ": no ghost block");
						else
						{
							int drawn, ghostly; Bounds gb;
							string odd = GhostLook(ghost, out drawn, out ghostly, out gb);
							Vector3 at = realAim.Value;
							float dist = Vector3.Distance(gb.ClosestPoint(at), at);
							string desc = "active " + ghost.gameObject.activeInHierarchy + ", " + ghostly + "/" + drawn + " renderers ghost-coloured, size " + gb.size.ToString("F2") + ", " + dist.ToString("F2") + " m from the spot";
							Check(ref itemOk, ghost.gameObject.activeInHierarchy && drawn > 0, name + ": the ghost is shown (" + desc + ")");
							Check(ref itemOk, drawn > 0 && ghostly == drawn, name + ": the ghost is drawn in Raft's ghost colour" + odd);
							Check(ref itemOk, dist < 1.5f && gb.size.magnitude < 12f, name + ": the ghost stands at the spot");
							if (shots)
							{
								try
								{
									Camera cam = Camera.main;
									Quaternion was = player.CameraTransform.rotation;
									player.CameraTransform.rotation = Quaternion.LookRotation(at - player.CameraTransform.position);
									string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "ghost_" + name + ".jpg"));
									try { File.WriteAllBytes(file, RenderCam(cam, 1280, 720).EncodeToJPG(88)); }
									finally { player.CameraTransform.rotation = was; }
									Log("PICTURE " + file);
								}
								catch (Exception e) { Log("place ghosts: picture: " + e.Message); }
							}
						}
					}
					// (Raft's own base item on the same spot, the same 10 frames: what a ghost does there in Raft itself)
					if (spot != null && u.Base != null && u.Base.settings_buildable != null && u.Base.settings_buildable.HasBuildablePrefabs)
					{
						into.SetItem(new ItemInstance(u.Base, 1, u.Base.MaxUses));
						if (hotbar != null) { hotbar.SetSelectedSlotIndex(player.Inventory.allSlots.IndexOf(into)); hotbar.ReselectCurrentSlot(); }
						yield return new WaitForSecondsRealtime(0.4f);
						int bShown = 0;
						var bErrs = new HashSet<string>();
						for (int f = 0; f < 10; f++)
						{
							yield return null;
							Block gNow = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
							if (gNow != null && gNow.gameObject.activeInHierarchy) bShown++;
							object e2 = gNow != null ? HarmonyLib.AccessTools.Method(typeof(BlockCreator), "CanBuildBlock").Invoke(bc, new object[] { gNow }) : null;
							bErrs.Add((Traverse.Create(bc).Field("quadAtCursor").GetValue<BlockQuad>() != null ? "quad " : "no quad ") + (e2 != null ? e2.ToString() : "no ghost"));
						}
						Log("place ghosts: " + name + ": Raft's " + u.Base.UniqueName + " there: shown " + bShown + "/10 frames, " + string.Join("; ", bErrs.ToArray()));
					}
					realAim = null;
					if (itemOk) good++; else { bad.Add(name); ok = false; }
					into.Reset();
					yield return new WaitForSecondsRealtime(0.2f);
				}
			}
			finally
			{
				realAim = null; realClickFrame = -1;
				if (into != null && !into.IsEmpty && ExtraUpgrades.FindMade(into.itemInstance.baseItem) != null) into.Reset();
				if (wall != null) BlockCreator.RemoveBlockNetwork(wall, null, true);
				foreach (Block b in grown) if (b != null) BlockCreator.RemoveBlockNetwork(b, null, true);
				CanvasHelper.ActiveMenu = menuWas;
				if (!optionWas) { opts.Remove(WorldOptions.Upgrades); WorldOptions.Set(opts); }
			}
			Log("place ghosts: " + good + "/" + items.Count + " mod items show their ghost" + (bad.Count > 0 ? "; without: " + string.Join(", ", bad.ToArray()) : ""));
			if (items.Count == 0) Fail("place ghosts: no mod items to place");
			else if (ok) Log("PASS: place ghosts"); else Fail("place ghosts");
		}

		// ---- GH2: placing for real, with nothing of the tests in Raft's way ----

		[ConsoleCommand(name: "CIPlaceRaw", docs: "Dev, world (host, a raft with foundations), GH2: run in a Raft no other CIPlace* test ran in (no test patch on Raft's aim or click). 'place': every placeable item the mod adds (and Raft's research table to compare) put in the hotbar as a pickup is, the player's own view turned to raft surfaces (MouseLook angles) until Raft's aim shows the ghost green, then placed as Raft's left click places it (BlockCreator.CreateBlock with the selected hotbar slot); the item must leave the hotbar; the blocks are listed in placeraw.txt. 'check' (after CISave and a reload): each listed block is there again with its item, then taken down")]
		public static void PlaceRawCommand(string[] args) { StartTest(PlaceRawRoutine(args ?? new string[0])); }

		static string PatchOwners(System.Reflection.MethodBase m)
		{
			if (m == null) return "(no method)";
			Patches p = Harmony.GetPatchInfo(m);
			if (p == null) return "none";
			return string.Join(",", p.Owners.ToArray());
		}

		static void LookAt(Network_Player player, Vector3 target)
		{
			Vector3 d = target - player.CameraTransform.position;
			float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
			// (MouseLook's yaw is the body's local one: the raft's turn taken off)
			Transform parent = player.transform.parent;
			if (parent != null) yaw -= parent.eulerAngles.y;
			Look(player, yaw, pitch);
		}

		static IEnumerator PlaceRawRoutine(string[] args)
		{
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost) { Fail("place raw: host, in a world"); yield break; }
			string listFile = Path.Combine(DynamicIslands.assetpath, "placeraw.txt");
			bool check = args.Any(a => a.Equals("check", StringComparison.OrdinalIgnoreCase));
			// what is patched on Raft's aim, surface check and click: only the mod's own patches may be there
			string aimOwners = PatchOwners(AccessTools.Method(typeof(BlockCreator), "GetQuadAtCursor"));
			string clickOwners = PatchOwners(AccessTools.Method(typeof(MyInput), "GetButtonDown", new[] { typeof(string) }));
			Log("place raw: patches - aim (GetQuadAtCursor): " + aimOwners + "; surfaces (AcceptsBlock): " + PatchOwners(AccessTools.Method(typeof(SO_BlockQuadType), "AcceptsBlock")) +
				"; overlap (IgnoresBlock): " + PatchOwners(AccessTools.Method(typeof(SO_BlockCollisionMask), "IgnoresBlock")) + "; click (MyInput.GetButtonDown): " + clickOwners +
				"; option upgrades " + WorldOptions.On(WorldOptions.Upgrades));
			Check(ref ok, !aimOwners.Contains("DevTests") && !clickOwners.Contains("DevTests.PlaceReal"), "place raw: no test patch on Raft's aim or click (a fresh Raft)");
			if (check)
			{
				string[] lines = File.Exists(listFile) ? File.ReadAllLines(listFile) : new string[0];
				Check(ref ok, lines.Length > 0, "place raw: " + lines.Length + " blocks listed in " + listFile);
				Block[] all = UnityEngine.Object.FindObjectsOfType<Block>();
				foreach (string line in lines)
				{
					string[] p = line.Split(' ');
					if (p.Length < 2) continue;
					uint idx; uint.TryParse(p[1], out idx);
					Block b = all.FirstOrDefault(x => x != null && x.ObjectIndex == idx);
					Check(ref ok, b != null && b.buildableItem != null && b.buildableItem.UniqueName == p[0], p[0] + ": there again after the reload (" + (b != null && b.buildableItem != null ? b.buildableItem.UniqueName : "no block " + idx) + ")");
					if (b != null && ExtraUpgrades.FindMade(b.buildableItem) != null) Check(ref ok, b.GetComponent<UpgradeBlock>() != null, p[0] + ": still an upgrade block");
					if (b != null) BlockCreator.RemoveBlockNetwork(b, null, true);
				}
				if (ok) Log("PASS: place raw check"); else Fail("place raw check");
				yield break;
			}
			BlockCreator bc = player.BlockCreator;
			Hotbar hotbar = player.Inventory.hotbar;
			var only = new HashSet<string>(args.Where(a => !a.Equals("place", StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
			List<Item_Base> items = ItemManager.GetAllItems().Where(i => i != null && ExtraUpgrades.FindMade(i) != null && i.settings_buildable != null && i.settings_buildable.HasBuildablePrefabs)
				.Where(i => only.Count == 0 || only.Contains(i.UniqueName)).OrderBy(i => i.UniqueIndex).ToList();
			Item_Base rt = ItemManager.GetItemByName("Placeable_ResearchTable");
			if (rt != null && (only.Count == 0 || only.Contains(rt.UniqueName))) items.Insert(0, rt);
			// (a new world's raft is 2 x 2: grown to 8 x 8 so that every item finds a free floor; kept, the reload needs it)
			Item_Base fItem = ItemManager.GetItemByName("Block_Foundation");
			List<Block> fs = UnityEngine.Object.FindObjectsOfType<Block>().Where(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName == "Block_Foundation").ToList();
			int grown = 0;
			if (fItem != null && fs.Count > 0)
			{
				Vector3 o = fs[0].transform.localPosition;
				for (int x = -3; x <= 4; x++)
					for (int z = -3; z <= 4; z++)
					{
						Vector3 p = o + new Vector3(x * 1.5f, 0f, z * 1.5f);
						if (fs.Any(b => (b.transform.localPosition - p).sqrMagnitude < 0.04f)) continue;
						try { if (bc.CreateBlockCheat(fItem, p, fs[0].transform.localEulerAngles, DPS.Default, -1) != null) grown++; }
						catch (Exception e) { Log("place raw: a foundation: " + e.Message); x = 99; break; }
					}
			}
			Log("place raw: " + fs.Count + " foundations, grown by " + grown + "; " + items.Count + " items");
			yield return new WaitForSecondsRealtime(0.5f);
			var placed = new List<string>();
			var bad = new List<string>();
			var used = new HashSet<BlockQuad>();
			CanvasHelper.ActiveMenu = MenuType.None;
			foreach (Item_Base item in items)
			{
				string name = item.UniqueName;
				bool itemOk = true;
				// (into the hotbar as a pickup puts it: Inventory.AddItem, then that slot selected)
				int had = player.Inventory.GetItemCount(name);
				player.Inventory.AddItem(name, 1);
				Slot slot = player.Inventory.allSlots.Take(player.Inventory.hotslotCount).FirstOrDefault(s => s != null && !s.IsEmpty && s.itemInstance.baseItem == item);
				if (slot == null) { Check(ref itemOk, false, name + ": not in the hotbar after AddItem"); bad.Add(name); ok = false; continue; }
				int slotIndex = player.Inventory.allSlots.IndexOf(slot);
				hotbar.SetSelectedSlotIndex(slotIndex); hotbar.ReselectCurrentSlot();
				yield return new WaitForSecondsRealtime(0.3f);
				Item_Base selected = Traverse.Create(bc).Field("selectedBuildableItem").GetValue<Item_Base>();
				Check(ref itemOk, selected == item, name + ": held, BlockCreator builds it (" + (selected != null ? selected.UniqueName : "none") + ")");
				// the view turned (MouseLook angles only) to each nearby raft surface that takes the item until the ghost is green
				Block ghost = null; BlockQuad green = null; string last = "no surface in reach";
				int seen = 0;
				// (the player walks the raft: stood on one foundation after another, the surfaces in reach from there)
				List<Block> floor = UnityEngine.Object.FindObjectsOfType<Block>().Where(b => b != null && b.buildableItem != null && b.buildableItem.UniqueName == "Block_Foundation").OrderBy(b => b.ObjectIndex).ToList();
				for (int stand = 0; stand < 10 && green == null && floor.Count > 0; stand++)
				{
				Block standOn = floor[(stand * 7 + placed.Count * 3) % floor.Count];
				player.transform.position = standOn.transform.position + Vector3.up * 1.2f;
				yield return new WaitForSecondsRealtime(0.3f);
				List<BlockQuad> quads = Resources.FindObjectsOfTypeAll<BlockQuad>().Where(q => q != null && q.gameObject.scene.IsValid() && q.quadType != null && !used.Contains(q) && FaceFor(q, item).HasValue
					&& (q.transform.position - player.CameraTransform.position).magnitude < 3.2f).OrderBy(q => (q.transform.position - player.transform.position).sqrMagnitude).Take(30).ToList();
				foreach (BlockQuad q in quads)
				{
					Vector3 at = SurfacePoint(q, FaceFor(q, item).Value);
					for (int f = 0; f < 4; f++) { LookAt(player, at); yield return null; }
					ghost = Traverse.Create(bc).Field("selectedBlock").GetValue<Block>();
					BlockQuad atCursor = Traverse.Create(bc).Field("quadAtCursor").GetValue<BlockQuad>();
					if (ghost != null && ghost.gameObject.activeInHierarchy) seen++;
					object err = ghost != null ? AccessTools.Method(typeof(BlockCreator), "CanBuildBlock").Invoke(bc, new object[] { ghost }) : null;
					last = (atCursor != null ? "aimed" : "no quad at cursor") + ", ghost " + (ghost == null ? "none" : ghost.gameObject.activeInHierarchy ? "shown" : "hidden") + ", " + (err != null ? err.ToString() : "-");
					if (atCursor != null && ghost != null && ghost.gameObject.activeInHierarchy && err != null && Convert.ToInt32(err) == 0) { green = atCursor; break; }
				}
				}
				Check(ref itemOk, green != null, name + ": the ghost shown green by Raft's own aim (" + seen + " surfaces showed it; last: " + last + ")");
				if (green != null)
				{
					// as Raft's left click on the host (BlockCreator.Update): the ghost's place, the surface's kind, the selected hotbar slot
					DPS dps = Traverse.Create(bc).Field("quadSurface").Field("dpsType").GetValue<DPS>();
					Block made = null;
					try
					{
						made = bc.CreateBlock(selected, ghost.transform.localPosition, ghost.transform.localEulerAngles, dps, hotbar.GetSelectedSlotIndex(), false,
							SaveAndLoad.GetUniqueObjectIndex(), ghost.networkedBehaviour != null ? SaveAndLoad.GetUniqueObjectIndex() : 0u, ghost.networkedBehaviour != null ? NetworkUpdateManager.GetUniqueBehaviourIndex() : 0u);
					}
					catch (Exception e) { Log("place raw: " + name + ": CreateBlock threw " + e.GetType().Name + ": " + e.Message); }
					yield return new WaitForSecondsRealtime(0.4f);
					int now = player.Inventory.GetItemCount(name);
					Check(ref itemOk, made != null && made.buildableItem == item, name + ": placed (" + (made != null && made.buildableItem != null ? made.buildableItem.UniqueName : "nothing") + ")");
					Check(ref itemOk, now == had, name + ": the item left the hotbar (" + (had + 1) + " -> " + now + ")");
					if (made != null) { placed.Add(name + " " + made.ObjectIndex); used.Add(green); }
				}
				int extra = player.Inventory.GetItemCount(name) - had;
				if (extra > 0) player.Inventory.RemoveItem(name, extra);
				if (!itemOk) { bad.Add(name); ok = false; }
				yield return new WaitForSecondsRealtime(0.2f);
			}
			try { File.WriteAllLines(listFile, placed.ToArray()); } catch (Exception e) { Log("place raw: list: " + e.Message); }
			Log("place raw: " + placed.Count + "/" + items.Count + " placed" + (bad.Count > 0 ? "; failed: " + string.Join(", ", bad.ToArray()) : ""));
			if (ok) Log("PASS: place raw"); else Fail("place raw");
		}
	}
}
