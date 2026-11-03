using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Fixes after the 11-02 release (ROADMAP 3h/3i, branch next-fixes): one test per fix.</summary>
	public static partial class DevTests
	{

		[ConsoleCommand(name: "CIStandKeysWide", docs: "Dev: RK9 - trade stand keys past object 8191 are their own (no two stands share a key, none in another range), and an island edit carries them across the 8191 line")]
		public static void StandKeysWideCommand()
		{
			bool ok = true;
			string tmp = Path.Combine(DynamicIslands.assetpath, "ci_standwide.island");
			try
			{
				var seen = new HashSet<int>();
				int clash = 0, outside = 0, back = 0;
				for (int i = 0; i < 0x10000; i++)
					for (int u = 0; u < TradeStand.MaxStock; u++)
					{
						int k = TradeStand.KeyFor(i, u), bi, bu;
						if (!seen.Add(k)) clash++;
						if (!((k >= TradeStand.KeyBase && k < TradeStand.KeyBase + 0x10000) || (k >= TradeStand.WideKeyBase && k < TradeStand.WideKeyBase + 0x80000))) outside++;
						if (!TradeStand.FromKey(k, out bi, out bu) || bi != i || bu != u) back++;
					}
				Check(ref ok, clash == 0 && outside == 0 && back == 0, "65536 stands x " + TradeStand.MaxStock + " units: " + clash + " shared keys, " + outside + " outside the two ranges, " + back + " not read back");
				Check(ref ok, TradeStand.KeyFor(5, 1) == TradeStand.KeyBase + 5 * TradeStand.MaxStock + 1, "a stand below 8192 keeps its old key (saves made before stay right)");
				Check(ref ok, TradeStand.KeyFor(8192, 0) != TradeStand.KeyFor(0, 0), "stand 8192 and stand 0 differ (the old mask made them one)");

				if (!PlaceableCatalog.IsBuilt) { Check(ref ok, false, "the object catalog isn't loaded yet"); }
				else
				{
					var f1 = new IslandFile { Name = "ci_standwide", TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
					for (int i = 0; i < 8195; i++)
						f1.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = new Vector3(i % 100, 5, i / 100), EulerRotation = Vector3.zero, Scale = Vector3.one });
					f1.Save(tmp);
					IslandFile a = IslandFile.Load(tmp);
					IslandFile b = IslandFile.Load(tmp);
					b.Objects.Insert(0, new IslandObject { Name = "Loot_Chest", Position = new Vector3(-5, 5, -5), EulerRotation = Vector3.zero, Scale = Vector3.one });
					StableIds.Prepare(b, a);
					b.Save(tmp);
					IslandFile c = IslandFile.Load(tmp);
					Func<bool, ObjectState> st = active => new ObjectState { Active = active, Yield = -1, Day = 3 };
					var was = new Dictionary<int, ObjectState> { { TradeStand.KeyFor(8191, 2), st(false) }, { TradeStand.KeyFor(8193, 0), st(true) } };
					var expect = new Dictionary<int, ObjectState> { { TradeStand.KeyFor(8192, 2), was[TradeStand.KeyFor(8191, 2)] }, { TradeStand.KeyFor(8194, 0), was[TradeStand.KeyFor(8193, 0)] } };
					int dropped;
					string unsure;
					string moved = StableIds.Carry(a, c, IslandObjectState.Encode(was), false, out dropped, out unsure);
					Check(ref ok, moved != null && moved == IslandObjectState.Encode(expect) && dropped == 0,
						"stand units carried over the 8191 line and past it (dropped " + dropped + ")\n    got    " + moved + "\n    wanted " + IslandObjectState.Encode(expect));
				}
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { try { File.Delete(tmp); } catch { } }
			if (ok) Log("PASS: stand keys wide"); else Fail("stand keys wide");
		}

		[ConsoleCommand(name: "CIClaimRelease", docs: "Dev, in a world (host): RK6 - a granted claim let go (a trade that can no longer be paid) ends the host's hold at once, for that player only; an older host's question path is unchanged")]
		public static void ClaimReleaseCommand()
		{
			bool ok = true;
			if (!Raft_Network.IsHost) { Fail("claim release: needs a world this Raft hosts"); return; }
			try
			{
				var e = new IslandWorldState.Entry { Id = 990061, Name = "ciclaimrelease", HostName = "ciclaimrelease" };
				int key = TradeStand.KeyFor(4, 0);
				ulong a = 76561190000000011UL, b = 76561190000000012UL;
				Check(ref ok, Claims.HostGrant(e, key, a) && !Claims.HostGrant(e, key, b), "player A is granted the stand's unit, B is told no while A holds it");
				// (B's let go: not B's hold - nothing changes)
				Claims.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = new[] { e.Id, 0 }, Index = key, Count = Claims.ReleaseMark }, b, x => { });
				Check(ref ok, Claims.IsHeld(e, key) && !Claims.HostGrant(e, key, b), "a let go from another player doesn't end A's hold");
				IslandNetMessage answer = null;
				Claims.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = new[] { e.Id, 0 }, Index = key, Count = Claims.ReleaseMark }, a, x => answer = x);
				Check(ref ok, answer == null, "a let go isn't answered");
				Check(ref ok, !Claims.IsHeld(e, key) && Claims.HostGrant(e, key, b), "A lets go (could no longer pay): B gets it at once, not 6 s later");
				// (the host's own grant, let go the same way)
				Claims.Release(e, key);
				Check(ref ok, Claims.IsHeld(e, key), "the host's Release doesn't end B's hold");
				int key2 = TradeStand.KeyFor(4, 1);
				Check(ref ok, Claims.May(e, key2, y => { }) && Claims.IsHeld(e, key2), "the host itself is granted another unit");
				Claims.Release(e, key2);
				Check(ref ok, !Claims.IsHeld(e, key2) && Claims.HostGrant(e, key2, a), "... and its own Release ends that hold at once");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { Claims.Reset(); }
			if (ok) Log("PASS: claim release"); else Fail("claim release");
		}

		[ConsoleCommand(name: "CIRadarCopyDone", docs: "Dev, in a test world 'CI ...' (host): RK8 - a plan's island (ruled) whose quest was done on a rule-less copy (spawned by hand) has no radar dot left; before the copy is done it has")]
		public static void RadarCopyDoneCommand()
		{
			bool ok = true;
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("radar copy done: run as the host in a test world 'CI ...'"); return; }
			const string name = "cirk8-radar";
			var made = new List<IslandWorldState.Entry>();
			try
			{
				IslandFile f = SampleIsland();
				f.Name = name;
				f.Props[IslandQuest.KeyTitle] = "Radar";
				f.Props[IslandQuest.KeySteps] = "reach|cizone|1|";
				f.Save(IslandSpawner.PathFor(name));
				Vector3 far = raft.Value + new Vector3(41000f, 0f, -41000f);
				far.y = 0f;
				IslandWorldState.Entry orig = IslandWorldState.Add(name, far, null, false), copy = IslandWorldState.Add(name, far + new Vector3(2000f, 0f, 0f), null, false);
				made.Add(orig); made.Add(copy);
				orig.Rule = "cirk8";
				orig.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				Func<bool> open = () => Traverse.Create(typeof(IslandRadar)).Method("StillOpen", orig).GetValue<bool>();
				Check(ref ok, open(), "the plan's island, begun, its copy not done: it has a dot");
				copy.State[WorldDirector.VisitKey] = new ObjectState { Active = true, Yield = 0, Day = 0 };
				QuestTracker.Set(copy, 1, 0, false);
				Check(ref ok, ReturningIslands.Finished(copy) && !open(), "the quest done on the rule-less copy: no dot for the plan's island");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally
			{
				if (made.Count > 0) IslandWorldState.RemoveIds(made.Select(e => e.Id).ToArray(), false);
				try { File.Delete(IslandSpawner.PathFor(name)); } catch { }
			}
			if (ok) Log("PASS: radar copy done"); else Fail("radar copy done");
		}

		[ConsoleCommand(name: "CIUnderSeaShift", docs: "Dev, anywhere: RK5 - an object moved across the island's own sea level counts as a shift of saved state (a flying world leaves objects under it out of the numbering); a zone moved so doesn't, nor a move above it")]
		public static void UnderSeaShiftCommand()
		{
			bool ok = true;
			try
			{
				Func<string, float, IslandObject> obj = (n, y) => new IslandObject { Name = n, Position = new Vector3(1, y, 1), EulerRotation = Vector3.zero, Scale = Vector3.one };
				Func<float, float, IslandFile> file = (chestY, zoneY) =>
				{
					var f = new IslandFile { Name = "ci_undersea", WaterLevel = IslandFile.DefaultWaterLevel };
					f.Objects.Add(obj("Loot_Chest", chestY)); f.Objects.Add(obj(ContentCatalog.TriggerZone, zoneY)); f.Objects.Add(obj("Loot_Chest", 30f));
					return f;
				};
				float above = IslandFile.DefaultWaterLevel + 5f, under = IslandFile.DefaultWaterLevel - 5f;
				Check(ref ok, !DynamicIslands.ObjectsShift(file(above, above), file(above + 3f, above)), "a chest moved, still above the island's sea: no shift");
				Check(ref ok, DynamicIslands.ObjectsShift(file(above, above), file(under, above)), "a chest moved under the island's sea: a shift (a flying world numbers the rest differently)");
				Check(ref ok, DynamicIslands.ObjectsShift(file(under, above), file(above, above)), "... and back up: a shift");
				Check(ref ok, !DynamicIslands.ObjectsShift(file(above, above), file(above, under)), "a zone moved under it: no shift (zones count either way)");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			if (ok) Log("PASS: under sea shift"); else Fail("under sea shift");
		}

		[ConsoleCommand(name: "CISpawnBoxStays", docs: "Dev, in game: RK16 - SPAWN TO RAFT clicked as a mouse does it (pointer down, up, click): its yes/no box and the Esc menu stay open until answered; No closes only the box")]
		public static void SpawnBoxStaysCommand() { StartTest(SpawnBoxStaysRoutine()); }

		static void PointerClick(GameObject g)
		{
			var ev = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left, clickCount = 1 };
			ev.position = RectTransformUtility.WorldToScreenPoint(null, g.transform.position);
			// (as a mouse: what the UI's raycast finds on top at that point gets the events, which go up to its button)
			var hits = new List<UnityEngine.EventSystems.RaycastResult>();
			if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.RaycastAll(ev, hits);
			if (hits.Count > 0)
			{
				Log("  click at " + ev.position + " hits " + string.Join(" > ", hits.Take(4).Select(h => h.gameObject.name + "[" + string.Join(",", h.gameObject.GetComponents<Component>().Select(x => x.GetType().Name).ToArray()) + "]").ToArray()));
				if (hits[0].gameObject.transform.IsChildOf(g.transform)) g = hits[0].gameObject;
				else Log("  (the top hit is not inside " + g.name + ")");
			}
			ev.pointerPress = g; ev.pointerEnter = g; ev.rawPointerPress = g;
			UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(g, ev, UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
			UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(g, ev, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
			UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(g, ev, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
			UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(g, ev, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
		}

		static IEnumerator SpawnBoxStaysRoutine()
		{
			bool ok = true;
			PauseMenu menu = UnityEngine.Object.FindObjectOfType<PauseMenu>();
			if (menu == null || RAPI.GetLocalPlayer() == null) { Fail("spawn box stays: no player or pause menu"); yield break; }
			SpawnToRaft.TraceClose = true;
			try
			{
				CallPrivate(menu, "Pause");
				yield return new WaitForSecondsRealtime(0.6f);
				UnityEngine.UI.Button b = SpawnToRaft.Button;
				if (b == null || !b.gameObject.activeInHierarchy) { Fail("spawn box stays: no SPAWN TO RAFT button in the menu"); yield break; }
				Log("  button parts: " + string.Join(", ", b.GetComponents<Component>().Select(x => x.GetType().Name).ToArray()));
				Log("  button's children: " + string.Join("; ", b.GetComponentsInChildren<Transform>(true).Where(x => x != b.transform).Select(x => x.name + "[" + string.Join(",", x.GetComponents<Component>().Select(y => y.GetType().Name).ToArray()) + "]").ToArray()));
				Log("  box parts: " + string.Join(", ", SpawnToRaft.Box.GetComponentsInChildren<Component>(true).Select(x => x.GetType().Name).Distinct().ToArray()));
				if (!b.interactable) { Fail("spawn box stays: the button is greyed (" + SpawnToRaft.WhyNot() + ")"); yield break; }
				PointerClick(b.gameObject);
				yield return new WaitForSecondsRealtime(3f);
				Check(ref ok, SpawnToRaft.Asking && CanvasHelper.ActiveMenu == MenuType.PauseMenu, "clicked with the mouse: 3 s later the box and the menu are still open (box " + SpawnToRaft.Asking + ", menu " + CanvasHelper.ActiveMenu + ")");
				if (SpawnToRaft.NoButton != null && SpawnToRaft.Asking)
				{
					PointerClick(SpawnToRaft.NoButton.gameObject);
					yield return new WaitForSecondsRealtime(1f);
					Check(ref ok, !SpawnToRaft.Asking && CanvasHelper.ActiveMenu == MenuType.PauseMenu && menu.buttonHolderPanel.activeSelf, "No clicked: only the box closes, the menu stays");
				}
			}
			finally
			{
				SpawnToRaft.TraceClose = false;
				try { if (CanvasHelper.ActiveMenu == MenuType.PauseMenu) menu.ContinuePlaying(); } catch { }
			}
			if (ok) Log("PASS: spawn box stays"); else Fail("spawn box stays");
		}

		[ConsoleCommand(name: "CIGodModeClick", docs: "Dev, editor: TN5 - the Caves panel's God mode tick box clicked as a mouse does it: God mode flips and the box shows it; clicked again, back")]
		public static void GodModeClickCommand() { StartTest(GodModeClickRoutine()); }

		static IEnumerator GodModeClickRoutine()
		{
			if (!DynamicIslands.InEditor()) { Fail("god mode click: in the editor"); yield break; }
			bool ok = true, god0 = EditorCamera.GodMode;
			EditorUI.SetTab(TAB.TerrainEdit);
			yield return null;
			yield return null;
			UnityEngine.UI.Button b = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>().FirstOrDefault(x => x.name == "Check_GodMode" && x.gameObject.scene.IsValid());
			if (b == null || !b.gameObject.activeInHierarchy) { Fail("god mode click: no God mode tick box shown on the terrain tab (" + (b == null ? "none" : "hidden") + ")"); yield break; }
			try
			{
				Check(ref ok, UIKit.IsChecked(b) == god0, "the box shows God mode as it is (" + god0 + ")");
				for (int i = 1; i <= 2; i++)
				{
					PointerClick(b.gameObject);
					yield return null;
					bool want = i == 1 ? !god0 : god0;
					Check(ref ok, EditorCamera.GodMode == want && UIKit.IsChecked(b) == want, "click " + i + ": God mode " + EditorCamera.GodMode + ", the box ticked " + UIKit.IsChecked(b) + " (want " + want + ")");
				}
			}
			finally { EditorCamera.GodMode = god0; UIKit.SetCheck(b, god0); }
			if (ok) Log("PASS: god mode click"); else Fail("god mode click");
		}

		[ConsoleCommand(name: "CIPickupLook", docs: "Dev, in game or editor: RK11 - logs what a quest item pickup shows (renderers, meshes, materials, parts) for Utopia's KeyRescue and KeyEntrance (or the names given), as listed and as placed")]
		public static void PickupLookCommand(string[] args) { StartTest(PickupLookRoutine(args != null && args.Length > 0 ? args : new[] { "QuestItemPickup_Utopia_KeyRescue", "QuestItemPickup_Utopia_KeyEntrance" })); }

		static void LogLook(string what, GameObject g)
		{
			Log("  " + what + ": active " + g.activeInHierarchy + ", scale " + g.transform.lossyScale + ", parts " + string.Join(",", g.GetComponents<Component>().Select(x => x.GetType().Name).ToArray()));
			foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
			{
				MeshFilter mf = r.GetComponent<MeshFilter>();
				Mesh m = mf != null ? mf.sharedMesh : r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : null;
				Log("    " + r.GetType().Name + " '" + r.name + "' enabled " + r.enabled + " active " + r.gameObject.activeInHierarchy + " layer " + LayerMask.LayerToName(r.gameObject.layer)
					+ " mesh " + (m == null ? "none" : m.name + " (" + m.vertexCount + " v, readable " + m.isReadable + ")") + " bounds " + r.bounds.size
					+ " mats " + string.Join("/", r.sharedMaterials.Select(x => x == null ? "null" : x.name + ":" + (x.shader != null ? x.shader.name : "?")).ToArray())
					+ " parts " + string.Join(",", r.GetComponents<Component>().Select(x => x.GetType().Name).ToArray()));
			}
			foreach (LODGroup l in g.GetComponentsInChildren<LODGroup>(true)) Log("    LODGroup '" + l.name + "' enabled " + l.enabled + " lods " + l.lodCount);
		}

		static IEnumerator PickupLookRoutine(string[] names)
		{
			Camera cam = Camera.main;
			var made = new List<GameObject>();
			PlaceableCatalog.Browse();
			foreach (string n in names) Log("  " + n + ": indexed " + PlaceableCatalog.IsIndexed(n) + ", scene " + PlaceableCatalog.SceneOf(n));
			yield return PlaceableCatalog.EnsureLoaded(names);
			foreach (string n in names) if (PlaceableCatalog.Get(n) == null) { Log("  " + n + " in Utopia's scene:"); yield return PlaceableCatalog.DescribeInScene("56#Landmark_Utopia#", n, Log); }
			try
			{
				foreach (string n in names)
				{
					GameObject proto = PlaceableCatalog.Get(n);
					if (proto == null) { Log("FAIL: " + n + ": not in the catalog"); yield break; }
					LogLook(n + " listed", proto);
					GameObject g = UnityEngine.Object.Instantiate(proto);
					g.SetActive(true);
					if (cam != null) g.transform.position = cam.transform.position + cam.transform.forward * 4f;
					made.Add(g);
					yield return new WaitForSecondsRealtime(1.5f);
					LogLook(n + " placed", g);
					if (!g.GetComponentsInChildren<Renderer>().Any(r => r.enabled && r.isVisible || r.enabled && r is MeshRenderer && r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null))
					{ Log("FAIL: " + n + " placed shows no model"); yield break; }
				}
			}
			finally { foreach (GameObject g in made) if (g != null) UnityEngine.Object.Destroy(g); }
			Log("PASS: pickup look: every pickup shows its model");
		}
	}
}
