using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests for delays and checks on events, story items and the journal, and movers in step (StoryItems.cs, Behaviours.cs).</summary>
	public static partial class DevTests
	{
		#region Editor

		[ConsoleCommand(name: "CIStoryTest", docs: "Dev, editor: checks and waits read and written, the story items window, story sets (locked door, trail of notes) and their undo, the item picker's story items, the checks in the Behaviour window, save and load")]
		public static void StoryTest()
		{
			DynamicIslands.instance.StartCoroutine(StoryTestRoutine());
		}

		static IEnumerator StoryTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;

			// Checks and waits as text
			List<ObjCheck> checks = ObjCheck.ParseLines("take|story:old-key|1\nhas|Plank|3\nstate|gate|closed\nsignal|horn\nquest|2\nbogus|x");
			Check(ref ok, checks.Count == 5 && checks[0].IsItem && checks[1].Count == 3 && checks[2].Arg == "closed" && checks[4].Target == "2", "checks are read (an unknown kind is left out)");
			Check(ref ok, ObjCheck.ParseLines(ObjCheck.ToLines(checks)).Select(c => c.Describe()).SequenceEqual(checks.Select(c => c.Describe())), "checks written and read back are the same");
			List<ObjAction> acts = ObjAction.ParseLines("open|gate|\nwait||2.5\nclose|gate|\nwait|4\njournal|The gate|It opened");
			Check(ref ok, acts.Count == 5 && Mathf.Approximately(acts[1].Seconds, 2.5f) && Mathf.Approximately(acts[3].Seconds, 4f) && acts[4].Shared && !acts[1].Shared, "waits (\"wait||2.5\" and \"wait|4\") and journal pages are read");
			Check(ref ok, BehaviourProps.EventKey("use!") == "else.use" && BehaviourProps.CheckKey("use") == "if.use" && BehaviourProps.Any(P("if.use", "has|x|1")), "the keys of checks and of what happens otherwise");
			Check(ref ok, StoryItems.IsStory("story:key") && StoryItems.IdOf("story: key ") == "key" && !StoryItems.IsStory("Plank"), "story item names");

			// The story items window
			EditorUI.SetTab(TAB.Island);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			var saved = new Dictionary<string, string>(DynamicIslands.currentIslandProps);
			DynamicIslands.currentIslandProps.Remove(StoryItems.Key);
			StoryItemsWindow.Open();
			yield return new WaitForSecondsRealtime(0.3f);
			Check(ref ok, StoryItemsWindow.IsOpen && StoryItemsWindow.Editing.Count == 0, "the Story items window opens (none yet)");
			Check(ref ok, StoryItems.QuestItems.Length > 20 && StoryItems.IconSprite(StoryItems.QuestIcon + "Vasagatan_GreenKey") != null, "Raft's quest item pictures are there (" + StoryItems.QuestItems.Length + ")");
			Click(new[] { "+ Add a story item" });
			yield return new WaitForSecondsRealtime(0.2f);
			Check(ref ok, StoryItemsWindow.Editing.Count == 1 && StoryItemsWindow.Editing[0].Id == "old-key", "adding a story item (id old-key)");
			yield return null;
			Screenshot(new[] { "story_window" });
			yield return new WaitForSecondsRealtime(0.6f); // (the picture is taken at the end of a later frame)

			// A story set: the locked door and its key (placed around the view, one undo step)
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, 0f, 500f);
			Camera.main.transform.position = c0 + new Vector3(0f, 60f, -30f);
			Camera.main.transform.LookAt(c0);
			List<GameObject> set = StorySets.LockedDoor(StoryItemsWindow.Editing, StorySets.ViewCentre());
			Check(ref ok, set.Count == 3 && StoryItemsWindow.Editing.Count == 2, "the locked door set: a door, a chest and a note, and a new story item");
			EditorGameObject door = set.Count > 0 ? set[0].GetComponent<EditorGameObject>() : null;
			string keyId = StoryItemsWindow.Editing.Count > 1 ? StoryItemsWindow.Editing[1].Id : "";
			Check(ref ok, door != null && ObjectProps.Get(door.Props, BehaviourProps.CheckKey("use")) == "has|story:" + keyId + "|1" && ObjectProps.Get(door.Props, BehaviourProps.ElseKey("use")).StartsWith("message||It's locked"),
				"the door asks for the key and says it's locked otherwise");
			EditorGameObject chest = set.Count > 1 ? set[1].GetComponent<EditorGameObject>() : null;
			Check(ref ok, chest != null && ObjectProps.Get(chest.Props, ObjectProps.LootItems) == "story:" + keyId + "*1", "the chest holds the key");
			List<GameObject> trail = StorySets.NoteTrail(StoryItemsWindow.Editing, StorySets.ViewCentre() + new Vector3(-40f, 0f, 0f));
			Check(ref ok, trail.Count == 4 && trail.Count(go => go.GetComponent<EditorGameObject>().GameObjectName.StartsWith("Note_")) == 3, "the trail of notes: three notes and a hidden chest");
			CommandUndoRedo.UndoRedoManager.Undo();
			Check(ref ok, trail.All(go => !go.activeSelf) && set.All(go => go.activeSelf), "undo takes the trail away (the door set stays)");
			CommandUndoRedo.UndoRedoManager.Redo();
			Check(ref ok, trail.All(go => go.activeSelf), "redo brings it back");
			StoryItemsWindow.SaveNow();
			yield return null;
			List<StoryItemDef> defs = StoryItems.Of(DynamicIslands.currentIslandProps);
			Check(ref ok, !StoryItemsWindow.IsOpen && defs.Count == 3 && defs.Any(d => d.Id == keyId && d.Icon.StartsWith(StoryItems.QuestIcon)), "saved: 3 story items on the island (" + string.Join(", ", defs.Select(d => d.Id).ToArray()) + ")");
			Check(ref ok, ContentCatalog.ItemLabel("story:" + keyId) == "Rusty key" && ContentCatalog.ItemSprite("story:" + keyId) != null, "story items have their name and picture wherever items show");

			// The item picker lists them first
			ItemPickerWindow.OpenFor(() => "", v => { });
			yield return new WaitForSecondsRealtime(0.4f);
			Transform storyGrid = EditorUI.Canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "StoryGrid");
			Check(ref ok, storyGrid != null && storyGrid.childCount == 3, "the item picker shows the island's 3 story items first (" + (storyGrid != null ? storyGrid.childCount : -1) + ")");
			Screenshot(new[] { "story_picker" });
			yield return new WaitForSecondsRealtime(0.6f);
			ItemPickerWindow.Close();

			// The door's checks in the Behaviour window
			if (door != null)
			{
				BehaviourWindow.Open(door);
				yield return new WaitForSecondsRealtime(0.5f);
				Transform part = EditorUI.Canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Checks" && t.gameObject.activeInHierarchy);
				Check(ref ok, part != null && part.GetComponentsInChildren<Transform>().Count(t => t.name == "Check") == 1, "the Behaviour window shows the door's check and what it says otherwise");
				Screenshot(new[] { "behaviour_checks" });
				yield return new WaitForSecondsRealtime(0.3f);
				BehaviourWindow.Close();
			}

			// Save and load keep the checks, waits and story items
			if (door != null) PropsCommand.Change(door, ObjectProps.With(door.Props, BehaviourProps.EventKey("use"), "switch||\nwait||3\nswitch||"));
			DynamicIslands.SaveIsland("cistory");
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("cistory"));
			IslandObject fd = f.Objects.FirstOrDefault(o => ObjectProps.Get(o.Props, BehaviourProps.CheckKey("use")).Length > 0);
			Check(ref ok, fd != null && ObjectProps.Get(fd.Props, BehaviourProps.EventKey("use")) == "switch||\nwait||3\nswitch||" && ObjectProps.Get(fd.Props, BehaviourProps.ElseKey("use")).Length > 0 &&
				StoryItems.Of(f.Props).Count == 3, "saved and read back: checks, otherwise, waits and story items");
			File.Delete(IslandSpawner.PathFor("cistory"));
			DynamicIslands.currentIslandProps.Clear();
			foreach (var kv in saved) DynamicIslands.currentIslandProps[kv.Key] = kv.Value;
			DynamicIslands.NewIsland();
			if (ok) Log("PASS: story items and checks in the editor"); else Fail("story items and checks in the editor");
		}

		#endregion

		#region World

		[ConsoleCommand(name: "CIStoryWorld", docs: "Dev, in game (host): a locked door opens only with the story item from a chest (and uses it up), a lever that needs planks, a gate that closes again after a wait, the journal (notes, pages, items), the client's path, saving the story, movers in step")]
		public static void StoryWorld()
		{
			DynamicIslands.instance.StartCoroutine(StoryWorldRoutine());
		}

		static IEnumerator StoryWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			StoryBook.Reset();

			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("generated_sample"));
			f.Name = "cistoryworld";
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * f.TerrainSize.y, z);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 25f);
			f.Props[StoryItems.Key] = "brass-key|Brass key|" + StoryItems.QuestIcon + "Vasagatan_GreenKey|A small brass key.";
			int doorIdx = f.Objects.Count;
			f.Objects.Add(new IslandObject { Name = "Block_Wall_Thatch", Position = ground(c.x + 6f, c.y), Props = P(BehaviourProps.Name, "vault", BehaviourProps.Turn, "90", BehaviourProps.MoveTime, "0.5",
				BehaviourProps.Use, "Open the vault", BehaviourProps.CheckKey("use"), "take|story:brass-key|1", BehaviourProps.ElseKey("use"), "message||It's locked.",
				BehaviourProps.EventKey("use"), "open||\nmessage||The key turns.\njournal|The vault|We opened the vault with the brass key.") });
			int chestIdx = f.Objects.Count;
			f.Objects.Add(new IslandObject { Name = "Loot_ChestSmall", Position = ground(c.x - 4f, c.y - 3f), Props = P(ObjectProps.LootItems, "story:brass-key*1", ObjectProps.NoteTitle, "Chest") });
			int gateIdx = f.Objects.Count;
			f.Objects.Add(new IslandObject { Name = "Block_Wall_Thatch", Position = ground(c.x + 6f, c.y + 6f), Props = P(BehaviourProps.Name, "gate", BehaviourProps.Move, "0,3,0", BehaviourProps.MoveTime, "0.3") });
			int leverIdx = f.Objects.Count;
			f.Objects.Add(new IslandObject { Name = "Log", Position = ground(c.x + 3f, c.y + 3f), Props = P(BehaviourProps.Name, "lever", BehaviourProps.Use, "Pull the lever",
				BehaviourProps.CheckKey("use"), "take|Plank|1\nstate|gate|closed", BehaviourProps.ElseKey("use"), "message||It needs a plank, and the gate must be closed.",
				BehaviourProps.EventKey("use"), "open|gate|\nwait||2\nclose|gate|\nmessage||The gate falls shut.") });
			int noteIdx = f.Objects.Count;
			f.Objects.Add(new IslandObject { Name = "Note_Paper", Position = ground(c.x - 2f, c.y + 4f), Props = P(ObjectProps.NoteTitle, "The keeper's note", ObjectProps.NoteText, "The key is in the small chest.") });
			int loopIdx = f.Objects.Count;
			f.Objects.Add(new IslandObject { Name = "Log", Position = ground(c.x - 6f, c.y) + Vector3.up, Props = P(BehaviourProps.Move, "0,2,0", BehaviourProps.MoveTime, "3", BehaviourProps.MoveMode, "loop",
				BehaviourProps.Spin, "40", BehaviourProps.Bob, "0.3", BehaviourProps.Collision, "none") });
			f.Save(IslandSpawner.PathFor("cistoryworld"));
			var created = new List<string> { "cistoryworld" };

			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius("cistoryworld"), 390f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile("cistoryworld", spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("the island did not spawn"); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			Func<int, bool> isOpen = i => { ObjectState s; return e.State.TryGetValue(Behaviours.StateBase + i, out s) && s.Yield == 1; };
			yield return EnsureAlive();
			yield return StandRoutine(e.Root);
			var sent = new List<IslandNetMessage>();
			IslandNetwork.Loopback = m => sent.Add(m);
			try
			{
				// The locked door without the key
				Behaviours.Fire(e, doorIdx, "use", true);
				Check(ref ok, !isOpen(doorIdx) && Behaviours.LastMessage == "It's locked." && (Behaviours.LastFailedCheck ?? "").Contains("Brass key"),
					"without the key the vault stays shut and says so (" + Behaviours.LastFailedCheck + ")");

				// The chest gives the story item to the crew
				LootCrate chest = obj(chestIdx) != null ? obj(chestIdx).GetComponentInChildren<LootCrate>(true) : null;
				if (chest != null) { chest.Open(); NoteReader.Close(); }
				Check(ref ok, StoryBook.Count("brass-key") == 1 && StoryBook.DefOf("brass-key") != null && StoryBook.DefOf("brass-key").Name == "Brass key", "the chest gives the crew the brass key (a story item)");
				Check(ref ok, sent.Any(m => m.Kind == IslandNetMessage.Story && m.Name == "all" && (m.Data ?? "").Contains("brass-key")), "the other players are sent the story state");

				// With the key: it opens, the key is used up, the journal gets a page
				Behaviours.Fire(e, doorIdx, "use", true);
				Check(ref ok, isOpen(doorIdx) && StoryBook.Count("brass-key") == 0 && Behaviours.LastMessage == "The key turns.", "with the key the vault opens and the key is used up");
				Check(ref ok, StoryBook.Pages.Any(p => p.Title == "The vault" && p.Text.Contains("brass key")), "the journal page from the action");
				Behaviours.Fire(e, doorIdx, "use", true);
				Check(ref ok, isOpen(doorIdx) && Behaviours.LastMessage == "It's locked.", "without a key left it doesn't work again");

				// A Raft item check, and a wait: the gate opens and falls shut 2 s later
				PlayerInventory inv = RAPI.GetLocalPlayer().Inventory;
				int have = inv.GetItemCount("Plank");
				if (have > 0) inv.RemoveItem("Plank", have);
				Behaviours.Fire(e, leverIdx, "use", true);
				Check(ref ok, !isOpen(gateIdx) && Behaviours.LastMessage.StartsWith("It needs a plank"), "the lever needs a plank");
				inv.AddItem("Plank", 3);
				Behaviours.Fire(e, leverIdx, "use", true);
				Check(ref ok, isOpen(gateIdx) && inv.GetItemCount("Plank") == 2, "with a plank: the gate opens at once and the plank is used up");
				Behaviours.Fire(e, leverIdx, "use", true);
				Check(ref ok, inv.GetItemCount("Plank") == 2 && Behaviours.LastMessage.StartsWith("It needs a plank"), "while the gate is open the lever does nothing (a state check)");
			}
			finally { IslandNetwork.Loopback = null; }
			yield return new WaitForSeconds(1f);
			Check(ref ok, isOpen(gateIdx), "after 1 s the gate is still open");
			yield return new WaitForSeconds(1.5f);
			Check(ref ok, !isOpen(gateIdx) && Behaviours.LastMessage == "The gate falls shut.", "after the 2 s wait the gate falls shut (and the message after the wait)");

			// A client's lever: the host does the shared part, with the wait
			Behaviours.OnEventMessage(e.Id, leverIdx, "use", false);
			Check(ref ok, isOpen(gateIdx), "a client's lever reaches the host: the gate opens");
			yield return new WaitForSeconds(2.5f);
			Check(ref ok, !isOpen(gateIdx), "... and falls shut after the wait on the host");
			// A client's story change reaches the host
			StoryBook.OnMessage(new IslandNetMessage { Kind = IslandNetMessage.Story, Name = "give", Data = "brass-key|2|Brass%20key||" });
			Check(ref ok, StoryBook.Count("brass-key") == 2, "a client's story change (2 keys) is applied by the host");

			// Reading a note puts it in the journal; the journal window
			CustomNote note = obj(noteIdx) != null ? obj(noteIdx).GetComponentInChildren<CustomNote>(true) : null;
			NoteReader.Open(note);
			yield return null;
			NoteReader.Close();
			Check(ref ok, StoryBook.Pages.Any(p => p.Title == "The keeper's note" && p.Text.Contains("small chest")), "a note read goes into the journal");
			JournalWindow.Open();
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, JournalWindow.IsOpen && JournalWindow.ShownTitle != null, "the journal opens (" + JournalWindow.ShownTitle + ")");
			Screenshot(new[] { "journal" });
			yield return new WaitForSeconds(0.5f);
			JournalWindow.Close();

			// The story is saved with the world
			List<string> lines = StoryBook.WriteLines().ToList();
			int pages = StoryBook.Pages.Count;
			StoryBook.Reset();
			foreach (string l in lines) { int eq = l.IndexOf('='); StoryBook.ReadLine(l.Substring(1, eq - 1), l.Substring(eq + 1)); }
			Check(ref ok, StoryBook.Count("brass-key") == 2 && StoryBook.Pages.Count == pages && StoryBook.Pages.Any(p => p.Text.Contains("\n") || p.Title == "The vault"), "the story state is written and read back (" + lines.Count + " lines)");

			// Movers in step: a second copy of the island moves exactly like the first
			Check(ref ok, SharedClock.Synced, "the shared clock follows the host's water time (" + SharedClock.Now.ToString("F1") + ")");
			int count = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile("cistoryworld", spot.Value + new Vector3(0f, 0f, 700f), true);
			IslandWorldState.Entry e2 = IslandWorldState.Islands.Skip(count).FirstOrDefault();
			yield return new WaitForSeconds(0.7f);
			IslandObjectRef a1 = obj(loopIdx), a2 = e2 != null && e2.Root != null ? e2.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == loopIdx) : null;
			bool same = a1 != null && a2 != null && (a1.transform.localPosition - a2.transform.localPosition).magnitude < 0.01f && Quaternion.Angle(a1.transform.localRotation, a2.transform.localRotation) < 0.5f;
			Check(ref ok, same, "two copies of the island show the looping, spinning, bobbing log in the same pose" + (a1 != null && a2 != null ? " (" + a1.transform.localPosition.y.ToString("F2") + " / " + a2.transform.localPosition.y.ToString("F2") + ")" : ""));

			yield return new WaitForSeconds(0.5f);
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(before).Select(x => x.Id).ToList(), true);
			foreach (string n in created) if (File.Exists(IslandSpawner.PathFor(n))) File.Delete(IslandSpawner.PathFor(n));
			StoryBook.Reset();
			if (ok) Log("PASS: story items, checks and waits in a world"); else Fail("story items, checks and waits in a world");
		}

		#endregion
	}
}
