using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;
using HMLLibrary;

namespace DynamicIslands
{
	/// <summary>Tests of the quest step kinds of 11-03 (QuestSteps): "Signal / object is" (QE1), up to 99 steps (QE2),
	/// "Bring items" (QE4) and "Craft or cook" (QE7).</summary>
	public static partial class DevTests
	{
		const string StepsIsland = "ciqsteps";

		[ConsoleCommand(name: "CIQuestStepsEditor", docs: "Dev, editor: QE1/2/4/7 - the quest editor's new step kinds (Signal / object is, Bring items, Craft or cook), their lists, up to 99 steps in a scroll box, the plan check's words, save and load")]
		public static void QuestStepsEditor() { StartTest(QuestStepsEditorRoutine()); }

		static IEnumerator QuestStepsEditorRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			DynamicIslands.currentIslandProps.Clear();
			yield return null;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, DynamicIslands.EditorWaterLevel + 1f, 500f);
			EditorGameObject lever = PlaceForTest("Note_Sign", c0, placed);
			PropsCommand.Change(lever, P(BehaviourProps.Name, "Lever1", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "signal||lever\nswitch|Gate|"));
			EditorGameObject gate = PlaceForTest(ContentCatalog.HelperWall, c0 + new Vector3(4, 0, 0), placed);
			PropsCommand.Change(gate, P(BehaviourProps.Name, "Gate", BehaviourProps.Move, "0,4,0", BehaviourProps.MoveMode, "switch"));
			EditorGameObject mara = PlaceForTest(Islanders.ObjectName, c0 + new Vector3(0, 0, 4), placed);
			PropsCommand.Change(mara, P(BehaviourProps.Name, "Mara"));
			EditorGameObject chest = PlaceForTest("Loot_Chest", c0 + new Vector3(-4, 0, 0), placed);
			NoteEditorWindow.Apply(chest, "Supplies", "");

			var q = new IslandQuest { Title = "CI steps", Intro = "Steps of every new kind.", Done = "Done." };
			q.Steps.Add(new IslandQuest.Step { Type = QuestSteps.Signal, Target = "Gate=open" });
			q.Steps.Add(new IslandQuest.Step { Type = QuestSteps.Signal, Target = "lever" });
			q.Steps.Add(new IslandQuest.Step { Type = QuestSteps.Bring, Target = "Plank@Mara", Count = 3 });
			q.Steps.Add(new IslandQuest.Step { Type = QuestSteps.Bring, Target = "Rope@Supplies", Count = 2 });
			q.Steps.Add(new IslandQuest.Step { Type = QuestSteps.Craft, Target = "Rope", Count = 2 });
			for (int i = 0; i < 7; i++) q.Steps.Add(new IslandQuest.Step { Type = "reach", Target = "spot" + i });
			QuestEditorWindow.Apply(q);
			IslandQuest back = IslandQuest.From(DynamicIslands.currentIslandProps);
			Check(ref ok, back.Steps.Count == 12, "a quest of 12 steps is kept (" + back.Steps.Count + ")");
			string words = string.Join(" / ", back.Steps.Take(5).Select(s => s.Describe()).ToArray());
			Check(ref ok, back.Steps[0].Describe() == "Open Gate" && back.Steps[2].Describe().Contains("Mara") && back.Steps[2].Describe().Contains("3") && back.Steps[4].Describe().StartsWith("Craft or cook"),
				"the new steps' words: " + words);
			Check(ref ok, IslandQuest.Types.Contains(QuestSteps.Signal) && IslandQuest.Types.Contains(QuestSteps.Bring) && IslandQuest.Types.Contains(QuestSteps.Craft) && IslandQuest.MaxStepCount == 99, "three new kinds, up to 99 steps");

			EditorUI.SetTab(TAB.Island);
			yield return null;
			QuestEditorWindow.Open();
			yield return new WaitForSecondsRealtime(0.5f);
			Transform qw = EditorUI.Canvas.transform.Find("QuestEditorWindow");
			Check(ref ok, qw != null, "the quest editor opens");
			if (qw == null) { Fail("quest steps editor"); yield break; }
			Transform box = qw.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "StepsBox");
			ScrollRect scroll = box != null ? box.GetComponentInChildren<ScrollRect>(true) : null;
			LayoutElement size = box != null ? box.GetComponent<LayoutElement>() : null;
			Check(ref ok, scroll != null && size != null && Mathf.Abs(size.preferredHeight - 324f) < 1f, "12 steps sit in a scroll box 10 rows high (" + (size != null ? size.preferredHeight.ToString() : "?") + ")");
			List<Button> kinds = qw.GetComponentsInChildren<Button>(true).Where(b => b.name == "Drop_StepType").ToList();
			string[] labels = kinds.Take(5).Select(b => UIKit.LabelOf(b).text).ToArray();
			Check(ref ok, kinds.Count == 12 && labels[0] == "Signal / object is" && labels[2] == "Bring items" && labels[4] == "Craft or cook", "the kinds' names: " + string.Join(", ", labels));
			var kindList = kinds.Count > 0 ? DropList.Shown(kinds[0]) : new List<KeyValuePair<string, string>>();
			Check(ref ok, kindList.Count == IslandQuest.Types.Length, "each step's kind list has all " + IslandQuest.Types.Length + " kinds (" + kindList.Count + ")");
			List<Button> picks = qw.GetComponentsInChildren<Button>(true).Where(b => b.name == "Pick_Target").ToList();
			Func<int, string> listed = i => i < picks.Count ? string.Join(",", DropList.Shown(picks[i]).Select(o => o.Key).ToArray()) : "?";
			string sigs = listed(0), brings = listed(2);
			Check(ref ok, sigs.Contains("lever") && sigs.Contains("Lever1=open") && sigs.Contains("Gate=open"), "the signal step's list: the lever's signal and the named objects (" + sigs + ")");
			Check(ref ok, brings.Contains("Plank@Mara") && brings.Contains("Plank@Supplies"), "the bring step's list: the islander and the titled chest (" + brings + ")");
			Screenshot(new[] { "qsteps_editor" });
			yield return new WaitForSecondsRealtime(0.5f);

			Button add = qw.GetComponentsInChildren<Button>(true).FirstOrDefault(b => UIKit.LabelOf(b) != null && UIKit.LabelOf(b).text == "+ Add a step");
			Check(ref ok, add != null, "the + Add a step button");
			// (one click a frame, as a builder clicks: the box is rebuilt per click)
			float slowest = 0f;
			if (add != null) for (int i = 0; i < 88; i++)
			{
				float t0 = Time.realtimeSinceStartup;
				add.onClick.Invoke();
				slowest = Mathf.Max(slowest, Time.realtimeSinceStartup - t0);
				yield return null;
			}
			Check(ref ok, slowest < 1f, "a click on + Add a step stays quick with 98 steps (slowest " + (slowest * 1000f).ToString("0") + " ms)");
			yield return null;
			int rows = qw.GetComponentsInChildren<Button>(true).Count(b => b.name == "Drop_StepType");
			Check(ref ok, rows == 99, "+ Add a step stops at 99 steps (" + rows + ")");
			Check(ref ok, scroll != null && scroll.verticalNormalizedPosition < 0.05f, "the box scrolled to the new step at the end (" + (scroll != null ? scroll.verticalNormalizedPosition.ToString("0.00") : "?") + ")");
			Screenshot(new[] { "qsteps_editor_99" });
			yield return new WaitForSecondsRealtime(0.5f);
			QuestEditorWindow.Close();

			// 99 steps save and load (the recipe's limit)
			var big = new IslandQuest { Title = "CI 99" };
			for (int i = 0; i < 99; i++) big.Steps.Add(new IslandQuest.Step { Type = i % 3 == 0 ? QuestSteps.Signal : i % 3 == 1 ? QuestSteps.Bring : QuestSteps.Craft, Target = i % 3 == 0 ? "s" + i : i % 3 == 1 ? "Plank@Mara" : "Rope", Count = i % 3 == 0 ? 1 : 2 });
			QuestEditorWindow.Apply(big);
			bool saved = DynamicIslands.SaveIsland(StepsIsland);
			DynamicIslands.currentIslandProps.Clear();
			DynamicIslands.LoadIsland(StepsIsland);
			yield return new WaitForSecondsRealtime(1f);
			IslandQuest loaded = IslandQuest.From(DynamicIslands.currentIslandProps);
			Check(ref ok, saved && loaded.Steps.Count == 99 && loaded.Steps[97].Type == QuestSteps.Bring && loaded.Steps[97].Target == "Plank@Mara" && loaded.Steps[97].Count == 2, "a quest of 99 steps saves and loads (" + loaded.Steps.Count + ")");

			// The plan check's words for the new kinds
			var f = new QuestSteps.PlanFacts();
			foreach (EditorGameObject g in new[] { lever, gate, mara, chest }) QuestSteps.AddFacts(f, g.GameObjectName, g.Props);
			bool warn;
			Check(ref ok, QuestSteps.StepProblem(f, QuestSteps.Signal, "lever", 1, out warn) == null && QuestSteps.StepProblem(f, QuestSteps.Signal, "Gate=open", 1, out warn) == null, "the plan check knows the lever's signal and the Gate");
			string far = QuestSteps.StepProblem(f, QuestSteps.Signal, "bell", 1, out warn);
			Check(ref ok, far != null && warn, "a signal nothing on the island sends is a warning: " + far);
			Check(ref ok, QuestSteps.StepProblem(f, QuestSteps.Signal, "Door9=open", 1, out warn) != null && !warn, "an unknown object is a problem");
			Check(ref ok, QuestSteps.StepProblem(f, QuestSteps.Bring, "Plank@Mara", 3, out warn) == null && QuestSteps.StepProblem(f, QuestSteps.Bring, "Plank@Nobody", 3, out warn) != null
				&& QuestSteps.StepProblem(f, QuestSteps.Bring, "Unobtainium@Mara", 3, out warn) != null, "bring: a known receiver is fine, an unknown one or item is a problem");
			Check(ref ok, QuestSteps.StepProblem(f, QuestSteps.Craft, "Rope", 2, out warn) == null && QuestSteps.StepProblem(f, QuestSteps.Craft, "Unobtainium", 2, out warn) != null, "craft: a Raft item is fine, an unknown one is a problem");

			File.Delete(IslandSpawner.PathFor(StepsIsland));
			DynamicIslands.currentIslandProps.Clear();
			if (ok) Log("PASS: quest steps editor"); else Fail("quest steps editor");
		}

		[ConsoleCommand(name: "CIQuestStepsWorld", docs: "Dev, in a world (host): QE1/4/7 - an island whose quest waits for a gate opened by a lever, the lever's signal, planks handed to an islander, rope put in a chest, rope crafted and food cooked")]
		public static void QuestStepsWorld() { StartTest(QuestStepsWorldRoutine()); }

		static IEnumerator QuestStepsWorldRoutine()
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Item_Base food = ItemManager.GetAllItems().FirstOrDefault(i => i.settings_cookable != null && i.settings_cookable.CookingResult != null && i.settings_cookable.CookingResult.item != null);
			string cooked = food != null ? food.settings_cookable.CookingResult.item.UniqueName : "";
			int lever = -1, mara = -1, chest = -1;
			IslandFile f = PlayerTestIsland((file, add) =>
			{
				lever = add("Note_Sign", -4f, 4f, P(BehaviourProps.Name, "Lever1", BehaviourProps.Use, "Pull", BehaviourProps.EventPrefix + "use", "signal||lever\nswitch|Gate|"));
				add(ContentCatalog.HelperWall, -8f, 4f, P(BehaviourProps.Name, "Gate", BehaviourProps.Move, "0,4,0", BehaviourProps.MoveMode, "switch"));
				mara = add(Islanders.ObjectName, 0f, 4f, P(BehaviourProps.Name, "Mara", Islanders.TextProp, "Bring me planks."));
				chest = add("Loot_Chest", 4f, 4f, P(ObjectProps.NoteTitle, "Supplies", ObjectProps.LootItems, "Nail*1"));
				file.Props[IslandQuest.KeyTitle] = "CI steps";
				file.Props[IslandQuest.KeySteps] = "signal|Gate=open|1|\nsignal|lever|1|\nbring|Plank@Mara|3|\nbring|Rope@Supplies|2|\ncraft|Rope|2|\ncraft|" + cooked + "|1|";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			PlayerInventory inv = RAPI.GetLocalPlayer().Inventory;
			Func<int, IEnumerator> waitStep = want => WaitStep(e, want, 5f);
			try
			{
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, QuestTracker.QuestOf(e).Steps.Count == 6 && QuestTracker.StepOf(e) == 0, "the island's quest has 6 steps, at step 1: " + QuestTracker.QuestOf(e).Steps[0].Describe());
				Screenshot(new[] { "qsteps_world" });
				// QE7: a craft before its step never counts later
				QuestSteps.Made("Rope", 5);
				yield return new WaitForSeconds(1.2f);
				Check(ref ok, QuestTracker.StepOf(e) == 0 && !QuestSteps.Holds(e, "Gate=open") && !QuestSteps.Holds(e, "lever"), "the gate is shut and no signal yet: step 1 waits");

				// QE1: the lever opens the gate (Gate=open) and sends "lever"
				IslandObjectRef lr = obj(lever);
				PutPlayerNear(lr.transform, 2f);
				Behaviours.Fire(e, lever, "use", true);
				yield return waitStep(1);
				Check(ref ok, QuestTracker.StepOf(e) >= 1, "pulling the lever opens the gate: step 1 (Gate=open) done");
				yield return waitStep(2);
				Check(ref ok, QuestTracker.StepOf(e) == 2, "the lever's signal came before its step: step 2 (signal lever) done at once");

				// QE4: planks to Mara - part, then the rest
				int planks0 = inv.GetItemCount("Plank");
				if (planks0 > 0) inv.RemoveItem("Plank", planks0);
				inv.AddItem("Plank", 2);
				IslandObjectRef mr = obj(mara);
				Islander isl = mr != null ? mr.GetComponentInChildren<Islander>(true) : null;
				Check(ref ok, isl != null, "Mara has her talk");
				if (isl != null) { PutPlayerNear(mr.transform, 2f); isl.Talk(); DialoguePanel.Close(); }
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, inv.GetItemCount("Plank") == 0 && QuestTracker.StepOf(e) == 2 && QuestTracker.ProgressOf(e, 0) == 2, "talking to Mara with 2 planks hands them over (2 of 3): \"" + QuestSteps.LastMessage + "\"");
				inv.AddItem("Plank", 5);
				if (isl != null) { isl.Talk(); DialoguePanel.Close(); }
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, inv.GetItemCount("Plank") == 4 && QuestTracker.StepOf(e) == 3, "with 5 more she takes only the 1 still wanted (4 left): step 3 done");
				Screenshot(new[] { "qsteps_bring" });

				// QE4: rope into the chest "Supplies" (not at another chest or islander)
				int rope0 = inv.GetItemCount("Rope");
				if (rope0 > 0) inv.RemoveItem("Rope", rope0);
				Check(ref ok, !QuestSteps.HandIn(e, "Mara") && QuestTracker.StepOf(e) == 3, "Mara doesn't take the rope meant for the chest");
				Check(ref ok, !QuestSteps.ChestRayed(e, "Supplies", false, null) && QuestSteps.ChestRayed(e, "Supplies", true, null) && !QuestSteps.ChestRayed(e, "Other", true, null),
					"without rope: the full chest 'Supplies' still opens, an empty one says \"Bring 2 x Rope here\", another chest is left alone");
				inv.AddItem("Rope", 3);
				Check(ref ok, QuestSteps.ChestRayed(e, "Supplies", false, null), "with rope: the chest 'Supplies' offers the hand-over first");
				IslandObjectRef cr = obj(chest);
				if (cr != null) PutPlayerNear(cr.transform, 2f);
				QuestSteps.HandIn(e, "Supplies");
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, inv.GetItemCount("Rope") == 1 && QuestTracker.StepOf(e) == 4, "handing 2 rope at the chest: step 4 done (1 rope left)");

				// QE7: craft rope at Raft's crafting menu (its own CraftItem, the cost taken)
				CraftingMenu menu = Resources.FindObjectsOfTypeAll<CraftingMenu>().FirstOrDefault(m => m.gameObject.scene.IsValid());
				Item_Base rope = ItemManager.GetItemByName("Rope");
				Check(ref ok, menu != null && rope != null && rope.settings_recipe != null, "Raft's crafting menu and the rope recipe");
				if (menu != null && rope != null)
				{
					SelectedRecipeBox sel = Traverse.Create(menu).Field("selectedRecipeBox").GetValue<SelectedRecipeBox>();
					Traverse.Create(sel).Field("selectedRecipeItem").SetValue(rope);
					int made0 = QuestSteps.Crafted;
					for (int i = 0; i < 2 && QuestTracker.StepOf(e) == 4; i++)
					{
						foreach (CostMultiple cm in rope.settings_recipe.NewCost) inv.AddItem(cm.items[0].UniqueName, cm.amount);
						menu.CraftItem();
						yield return new WaitForSeconds(0.5f);
					}
					Traverse.Create(sel).Field("selectedRecipeItem").SetValue(null);
					Check(ref ok, QuestSteps.Crafted > made0 && QuestSteps.LastMade == "Rope", "Raft's CraftItem tells the quest (" + (QuestSteps.Crafted - made0) + " crafts)");
				}
				Check(ref ok, QuestTracker.StepOf(e) == 5, "crafting 2 rope (the 5 made before the step don't count): step 5 done (" + QuestTracker.StepOf(e) + ")");

				// QE7: cooking - the hooks on grills/smelters/purifiers and pots, and what they report
				Func<Type, string, string, bool> hooked = (t, m, by) =>
				{
					Patches pi = Harmony.GetPatchInfo(AccessTools.Method(t, m));
					return pi != null && pi.Postfixes.Any(x => x.PatchMethod.DeclaringType.Name == by);
				};
				Check(ref ok, hooked(typeof(Block_CookingStand), "CollectItem", "QuestStepsCook") && hooked(typeof(CookingTable), "PickupFood", "QuestStepsPot") && hooked(typeof(CraftingMenu), "CraftItem", "QuestStepsCraft"), "crafting, a cooking stand's collect and a pot's pickup are hooked");
				QuestSteps.Made("Plank", 1);
				Check(ref ok, QuestTracker.StepOf(e) == 5, "another item doesn't count for the cook step");
				Vector3 home = RAPI.GetLocalPlayer().transform.position;
				IslandInfoTag tag = e.Root.GetComponent<IslandInfoTag>();
				Vector3 mid = e.Root.transform.position + tag.LocalCentre;
				Vector3 away = new Vector3(home.x - mid.x, 0f, home.z - mid.z).normalized;
				if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
				yield return PutPlayer(RAPI.GetLocalPlayer(), mid + away * (tag.Radius + QuestSteps.CraftReach + 60f), true);
				QuestSteps.Made(cooked, 1);
				Check(ref ok, QuestTracker.StepOf(e) == 5, "cooking far off the island doesn't count");
				yield return StandRoutine(e.Root);
				yield return new WaitForSeconds(1f);
				QuestSteps.Made(cooked, 1);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, QuestTracker.StepOf(e) == 6, "cooking " + cooked + " on the island finishes the quest (" + QuestTracker.LastMessage + ")");
				Screenshot(new[] { "qsteps_done" });
				yield return new WaitForSeconds(0.6f);
			}
			finally { RemovePlayerIsland(e); }
			if (ok) Log("PASS: quest steps in a world"); else Fail("quest steps in a world");
		}

		static IEnumerator WaitStep(IslandWorldState.Entry e, int want, float seconds)
		{
			for (float t = 0; t < seconds && QuestTracker.StepOf(e) < want; t += 0.25f) yield return new WaitForSeconds(0.25f);
		}
	}
}
