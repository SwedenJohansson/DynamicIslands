using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Quest step kinds of 11-03 (ROADMAP 3k):
	///   - "signal" (QE1, "Signal / object is"): waits for a signal (a lever's "lever", the generator's "power", "lights:&lt;group&gt;",
	///     an action's own) or an object's state ("Gate=open", closed, shown, hidden). Counted by the host like story items:
	///     a signal sent before the step came counts when it comes.
	///   - "bring" (QE4, "Bring items"): N of a Raft item handed to an islander (talking to them) or a chest (its title):
	///     target "Plank@Mara" (no "@": to any islander or chest of the island). The items are taken from the player.
	///   - "craft" (QE7, "Craft or cook"): N of a Raft item crafted, cooked (grill, purifier, cooking pot) while the step is
	///     the quest's current one, at the island. Never early: what was made before the step came doesn't count.
	///   - "fish" (11-04 gap idea 3, "Catch fish"): N fish (of a kind, or any) pulled out with Raft's fishing rod at the island
	///     (from its shore or a raft moored off it, CraftReach) while the step is the current one.
	///   - "gather" (11-04 gap idea 4, "Gather / harvest"): N of a Raft item picked up, dug or harvested by hand, shovel or axe
	///     at the island (GatherReach) while the step is the current one: what came into the player's inventory.
	/// Bring, craft, fish and gather go through QuestTracker.Event like a note read: a player's machine tells the host, everyone sees it.
	/// </summary>
	public static class QuestSteps
	{
		public const string Signal = "signal", Bring = "bring", Craft = "craft", Fish = "fish", Gather = "gather";
		/// <summary>How far from the island's land a craft or a catch still counts for its quest (a raft moored off it).</summary>
		internal const float CraftReach = 300f;
		/// <summary>How far from the island's land a pickup still counts for a gather step (its beach and shallows).</summary>
		internal const float GatherReach = 40f;

		public static bool Counted(string type) { return type == Signal; }
		/// <summary>Steps with a number of things (the editor's count field).</summary>
		public static bool HasCount(string type) { return type == Bring || type == Craft || type == Fish || type == Gather; }
		/// <summary>Steps whose target is a Raft item (the editor's … item picker).</summary>
		public static bool TakesItem(string type) { return type == Bring || type == Craft || type == Fish || type == Gather; }

		/// <summary>"Gate=open" = an object's state: the object's name and the state; else false (a signal's name).</summary>
		public static bool IsState(string target, out string obj, out string state)
		{
			obj = state = "";
			target = (target ?? "").Trim();
			int eq = target.LastIndexOf('=');
			if (eq <= 0) return false;
			string s = target.Substring(eq + 1).Trim().ToLowerInvariant();
			if (!BehaviourProps.States.Contains(s)) return false;
			obj = target.Substring(0, eq).Trim();
			state = s;
			return obj.Length > 0;
		}

		/// <summary>"Plank@Mara": the item and who takes it ("" = any islander or chest).</summary>
		public static void SplitBring(string target, out string item, out string receiver)
		{
			target = (target ?? "").Trim();
			int at = target.IndexOf('@');
			item = (at >= 0 ? target.Substring(0, at) : target).Trim();
			receiver = at >= 0 ? target.Substring(at + 1).Trim() : "";
		}

		static string Item(string name, int count) { return (count > 1 ? count + " \u00D7 " : "") + (name.Length > 0 ? ContentCatalog.ItemLabel(name) : "anything"); }

		/// <summary>A step's words (Step.Describe), or null.</summary>
		internal static string Describe(string type, string target, int count)
		{
			switch (type)
			{
				case Signal:
					string obj, state;
					if (IsState(target, out obj, out state))
						return state == "open" ? "Open " + obj : state == "closed" ? "Close " + obj : state == "shown" ? "Make " + obj + " appear" : "Make " + obj + " disappear";
					string t = target.Trim().ToLowerInvariant();
					if (t.Length == 0) return "Set something off";
					if (t == "lever") return "Pull the lever";
					if (t == "crank") return "Turn the crank";
					if (t == "power") return "Get the power on";
					if (t == "radio") return "Get the radio working";
					if (t.StartsWith("lights:")) return "Light all the lights of " + target.Trim().Substring(7);
					if (t.StartsWith("set:")) return "Find the whole " + target.Trim().Substring(4);
					return "Set off \"" + target.Trim() + "\"";
				case Bring:
					string item, to;
					SplitBring(target, out item, out to);
					return "Bring " + Item(item, count) + (to.Length > 0 ? " to " + to : "");
				case Craft:
					return "Craft or cook " + Item(target.Trim(), count);
				case Fish:
					return target.Trim().Length == 0 ? "Catch " + (count > 1 ? count + " fish" : "a fish") + " with the rod" : "Catch " + Item(target.Trim(), count) + " with the rod";
				case Gather:
					return target.Trim().Length == 0 ? "Gather " + (count > 1 ? count + " things" : "something") + " here" : "Gather " + Item(target.Trim(), count);
			}
			return null;
		}

		#region Signal (host, counted)

		/// <summary>Host: how far a signal step is (its count when it holds, else 0).</summary>
		internal static int Found(IslandWorldState.Entry e, IslandQuest.Step s)
		{
			return s.Type == Signal && Holds(e, s.Target) ? Mathf.Max(1, s.Count) : 0;
		}

		/// <summary>Whether the island has had the signal, or its object is in the state (only while it is loaded here).</summary>
		public static bool Holds(IslandWorldState.Entry e, string target)
		{
			if (e == null) return false;
			string obj, state;
			if (IsState(target, out obj, out state))
			{
				if (e.Root == null || Behaviours.TargetsOf(e, -1, obj).Count == 0) return false;
				return Behaviours.Passes(e, -1, new List<ObjCheck> { new ObjCheck { Kind = "state", Target = obj, Arg = state } }, false, false);
			}
			return target.Trim().Length > 0 && Behaviours.HasSignal(e, target.Trim());
		}

		/// <summary>The signals objects with these settings send: their actions' "signal", a ready piece's, a light group's,
		/// a collectible set's, a boss's phases, a survivor's cage.</summary>
		public static IEnumerable<string> SignalsOf(string objectName, IDictionary<string, string> p)
		{
			if (p == null) yield break;
			foreach (var kv in p)
				if (kv.Key.StartsWith(BehaviourProps.EventPrefix) || kv.Key.StartsWith(BehaviourProps.ElsePrefix))
					foreach (ObjAction a in ObjAction.ParseLines(kv.Value))
						if (a.Verb == "signal" && a.Arg.Trim().Length > 0) yield return a.Arg.Trim();
			if (Lights.IsLight(objectName, p) && Lights.GroupOf(p).Length > 0) yield return Lights.SignalOf(Lights.GroupOf(p));
			if (Collectibles.IsPiece(p)) yield return Collectibles.SignalOf(Collectibles.SetOf(p));
			if (ObjectProps.Get(p, BossPhases.Share).Trim().Length > 0) yield return BossPhases.SignalOf(p);
			if (Survivors.IsCage(objectName)) yield return Survivors.SignalOf(p);
		}

		#endregion

		#region Bring (every machine: the local player hands items over)

		/// <summary>The bring step of the island waiting for this receiver (an islander's name, a chest's title), or null:
		/// quest n, the item, how many more.</summary>
		public static bool Wants(IslandWorldState.Entry e, string receiver, out int quest, out string item, out int more)
		{
			bool named;
			return Wants(e, receiver, out quest, out item, out more, out named);
		}

		/// <summary>named: the step names this receiver (not "any islander or chest").</summary>
		public static bool Wants(IslandWorldState.Entry e, string receiver, out int quest, out string item, out int more, out bool named)
		{
			quest = -1; item = ""; more = 0; named = false;
			if (e == null) return false;
			string who = IslandQuest.Clean(receiver);
			for (int n = 0, count = QuestTracker.QuestsOf(e); n < count; n++)
			{
				IslandQuest q = QuestTracker.QuestOf(e, n);
				int step = QuestTracker.StepOf(e, n);
				if (!q.Exists || step >= q.Steps.Count || q.Steps[step].Type != Bring) continue;
				string it, to;
				SplitBring(q.Steps[step].Target, out it, out to);
				if (it.Length == 0 || (to.Length > 0 && !to.Equals(who, StringComparison.OrdinalIgnoreCase))) continue;
				quest = n; item = it; more = Mathf.Max(1, q.Steps[step].Count - QuestTracker.ProgressOf(e, n)); named = to.Length > 0;
				return true;
			}
			return false;
		}

		/// <summary>The local player hands over what a bring step wants of this receiver (what they have of it, up to what it
		/// still needs): taken from them, counted for the quest. True when something was handed over.</summary>
		public static bool HandIn(IslandWorldState.Entry e, string receiver)
		{
			Network_Player p = RAPI.GetLocalPlayer();
			if (p == null || p.Inventory == null) return false;
			bool any = false;
			for (int guard = 0; guard < 10; guard++)
			{
				int n, more;
				string item;
				if (!Wants(e, receiver, out n, out item, out more)) break;
				int have = p.Inventory.GetItemCount(item);
				if (have <= 0)
				{
					if (!any) Say(receiver + " wants " + Item(item, more) + ".");
					break;
				}
				int give = Mathf.Min(have, more);
				p.Inventory.RemoveItem(item, give);
				IslandQuest.Step s = QuestTracker.QuestOf(e, n).Steps[QuestTracker.StepOf(e, n)];
				Debug.Log("[CUSTOM ISLANDS] [quest] " + e.Name + ": handed " + give + " " + item + " to '" + receiver + "'");
				Say("You hand over " + Item(item, give) + (give < more ? " (" + (more - give) + " more wanted)" : "") + ".");
				QuestTracker.Event(e, n, Bring, s.Target, give);
				any = true;
				if (give < more) break;
			}
			return any;
		}

		/// <summary>A chest's hint and press for a bring step (LootCrate): true when the chest wants items, so its own hint waits.</summary>
		internal static bool ChestRayed(IslandWorldState.Entry e, string title, bool emptyChest, DisplayTextManager hints)
		{
			int n, more;
			string item;
			bool named;
			if (!Wants(e, title, out n, out item, out more, out named)) return false;
			// (a step for any chest doesn't stand before a chest's own loot: only an empty one takes the items)
			if (!named && !emptyChest) return false;
			Network_Player p = RAPI.GetLocalPlayer();
			int have = p != null && p.Inventory != null ? p.Inventory.GetItemCount(item) : 0;
			if (have <= 0)
			{
				// (nothing to hand over: an empty chest says what it wants, a full one still opens)
				if (!emptyChest) return false;
				if (hints != null) hints.ShowText("Bring " + Item(item, more) + " here", 0, true, 0);
				return true;
			}
			if (hints != null) hints.ShowText("Hand over " + Item(item, Mathf.Min(have, more)), CustomNote.InteractKey, 0, 0, true);
			if (CustomNote.InteractPressed())
			{
				if (hints != null) hints.HideDisplayTexts();
				HandIn(e, title);
			}
			return true;
		}

		/// <summary>A player: the host gave back items of this player's hand-over it didn't count (IslandNetwork QuestItemsBack;
		/// why "over": another player brought the rest in the same moment, "late": the step was done already, "refused": the host
		/// didn't count it). Into the bag; what doesn't fit is dropped in front of them.</summary>
		internal static void ItemsBack(string item, int amount, string why)
		{
			if (string.IsNullOrEmpty(item) || amount <= 0 || amount > 9999 || ItemManager.GetItemByName(item) == null) return;
			TriggerZone.Give(new[] { new KeyValuePair<string, int>(item, amount) });
			Debug.Log("[CUSTOM ISLANDS] [quest] the host gave back " + amount + " " + item + " (" + why + ")");
			Say(Item(item, amount) + " given back: " + (why == "over" ? "another player brought the rest" : why == "late" ? "that step was already done" : "the host didn't count it") + ".");
		}

		static void Say(string text)
		{
			LastMessage = text;
			IslandInfo.ShowMessage(text);
		}

		/// <summary>The last hand-over message (tests).</summary>
		public static string LastMessage { get; private set; }

		#endregion

		#region Craft (every machine: what the local player crafts or cooks)

		/// <summary>The local player crafted or cooked count of an item: counts for craft steps waiting for it (now, never
		/// before) on the islands the player is at.</summary>
		public static void Made(string item, int count)
		{
			if (string.IsNullOrEmpty(item) || count <= 0) return;
			Crafted++;
			LastMade = item;
			Credit(Craft, CraftReach, item, count, "made");
		}

		/// <summary>The local player caught a fish with the rod: counts for fish steps waiting for it (now, never before).</summary>
		public static void Caught(string item, int count)
		{
			if (string.IsNullOrEmpty(item) || count <= 0) return;
			Fished++;
			LastCaught = item;
			Credit(Fish, CraftReach, item, count, "caught");
		}

		/// <summary>The local player picked up, dug or harvested count of an item: counts for gather steps waiting for it.</summary>
		public static void Gathered(string item, int count)
		{
			if (string.IsNullOrEmpty(item) || count <= 0) return;
			GatheredCount++;
			LastGathered = item;
			Credit(Gather, GatherReach, item, count, "gathered");
		}

		/// <summary>Counts for the current step of this kind of every quest of the islands within reach that wants the item.</summary>
		static void Credit(string kind, float reach, string item, int count, string verb)
		{
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.ToList())
			{
				if (e.Root == null || !QuestTracker.Near(e, reach)) continue;
				for (int n = 0, quests = QuestTracker.QuestsOf(e); n < quests; n++)
				{
					IslandQuest q = QuestTracker.QuestOf(e, n);
					int step = QuestTracker.StepOf(e, n);
					if (!q.Exists || step >= q.Steps.Count || q.Steps[step].Type != kind) continue;
					string want = q.Steps[step].Target.Trim();
					if (want.Length > 0 && !want.Equals(item, StringComparison.OrdinalIgnoreCase)) continue;
					if (want.Length == 0 && kind == Fish && !IsFish(item)) continue; // (any fish: not the rod's junk, a lucky cat or a candle)
					Debug.Log("[CUSTOM ISLANDS] [quest] " + e.Name + ": " + verb + " " + count + " " + item + " for step " + (step + 1));
					QuestTracker.Event(e, n, kind, want.Length > 0 ? want : item, count);
				}
			}
		}

		/// <summary>Whether an island within GatherReach waits for a gather step now (the pickup hooks count only then).</summary>
		internal static bool GatherWaiting()
		{
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null) continue;
				for (int n = 0, quests = QuestTracker.QuestsOf(e); n < quests; n++)
				{
					IslandQuest q = QuestTracker.QuestOf(e, n);
					int step = QuestTracker.StepOf(e, n);
					if (q.Exists && step < q.Steps.Count && q.Steps[step].Type == Gather && QuestTracker.Near(e, GatherReach)) return true;
				}
			}
			return false;
		}

		/// <summary>Fish and gather counts this session (tests), and the last item of each.</summary>
		public static int Fished { get; private set; }
		public static string LastCaught { get; private set; }
		public static int GatheredCount { get; private set; }
		public static string LastGathered { get; private set; }

		static List<string> fishNames;

		/// <summary>A fish ("any fish" steps): Raft's raw fish (Raw_...) and trophy fish, not the other things its rod brings up.</summary>
		internal static bool IsFish(string item) { return item != null && (item.StartsWith("Raw_", StringComparison.OrdinalIgnoreCase) || item.StartsWith("TrophyFish_", StringComparison.OrdinalIgnoreCase)); }

		/// <summary>The fish Raft's rod catches (its bait pools), or a fixed list while none is loaded.</summary>
		public static List<string> FishNames()
		{
			if (fishNames != null) return fishNames;
			var names = new List<string>();
			try
			{
				foreach (FishingBaitHandler h in Resources.FindObjectsOfTypeAll<FishingBaitHandler>())
				{
					var droppers = new List<RandomDropper>
					{
						Traverse.Create(h).Field("defaultDropper").GetValue<RandomDropper>(), Traverse.Create(h).Field("metalRodDropper").GetValue<RandomDropper>()
					};
					var links = Traverse.Create(h).Field("baitConnections").GetValue<List<BaitConnection>>();
					if (links != null) droppers.AddRange(links.Where(b => b != null).Select(b => b.dropper));
					foreach (RandomDropper d in droppers)
					{
						if (d == null || d.randomDropperAsset == null || d.randomDropperAsset.randomizer == null || d.randomDropperAsset.randomizer.items == null) continue;
						foreach (RandomItem it in d.randomDropperAsset.randomizer.items)
						{
							Item_Base b = it != null ? it.obj as Item_Base : null;
							if (b != null && !names.Contains(b.UniqueName)) names.Add(b.UniqueName);
						}
					}
					if (names.Count > 0) break;
				}
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] [quest fish] " + ex.Message); }
			if (names.Count > 0) { fishNames = names; return names; }
			return new List<string> { "Raw_Pomfret", "Raw_Mackerel", "Raw_Herring", "Raw_Tilapia", "Raw_Catfish", "Raw_Salmon" };
		}

		/// <summary>Crafts and cooks this session (tests), and the last item.</summary>
		public static int Crafted { get; private set; }
		public static string LastMade { get; private set; }

		#endregion

		#region Editor and plan check

		/// <summary>The editor's \u25BE beside a step of these kinds (target: the step's now), or null.</summary>
		internal static List<DropList.Option> NamesFor(string type, string target, IEnumerable<EditorGameObject> all)
		{
			switch (type)
			{
				case Signal:
				{
					var list = new List<DropList.Option>();
					var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (EditorGameObject g in all)
						foreach (string sig in SignalsOf(g.GameObjectName, g.Props))
							if (seen.Add(sig)) list.Add(new DropList.Option(sig, sig, "A signal " + PlaceableCatalog.DisplayName(g.GameObjectName) + " sends"));
					foreach (EditorGameObject g in all)
					{
						string name = ObjectProps.Get(g.Props, BehaviourProps.Name).Trim();
						if (name.Length == 0 || !seen.Add(name + "=")) continue;
						list.Add(new DropList.Option(name + "=open", name + " is open", "The object '" + name + "' opened (switched, turned). Change \"open\" to closed, shown or hidden"));
						list.Add(new DropList.Option(name + "=shown", name + " is shown", "The object '" + name + "' showing (an action showed it)"));
					}
					return list;
				}
				case Bring:
				{
					string item, to;
					SplitBring(target, out item, out to);
					var names = all.Where(g => Islanders.Is(g.GameObjectName)).Select(g => Islanders.NameOf(g.Props))
						.Concat(all.Where(g => ObjectProps.IsLoot(g.GameObjectName, g.Props)).Select(g => ObjectProps.Get(g.Props, ObjectProps.NoteTitle).Trim()))
						.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
					return names.Select(n => new DropList.Option(item + "@" + n, n, "Hand the items to " + n + " (an islander: by talking; a chest: at the chest)")).ToList();
				}
				case Fish:
				{
					var list = new List<DropList.Option> { new DropList.Option("", "Any fish", "Any fish the rod brings up counts (raw or trophy fish; not the other things it pulls, a lucky cat or a candle)") };
					list.AddRange(FishNames().Select(n => new DropList.Option(n, ContentCatalog.ItemLabel(n), "Only " + ContentCatalog.ItemLabel(n) + " counts")));
					return list;
				}
			}
			return null;
		}

		/// <summary>Facts a plan check knows for these kinds: the signals an island's objects send, its named objects, its
		/// islanders and titled chests.</summary>
		public class PlanFacts
		{
			public readonly HashSet<string> Signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				Receivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public int ReceiverCount;
		}

		public static void AddFacts(PlanFacts f, string objectName, IDictionary<string, string> p)
		{
			foreach (string s in SignalsOf(objectName, p)) f.Signals.Add(s);
			string name = ObjectProps.Get(p, BehaviourProps.Name).Trim();
			if (name.Length > 0) f.Objects.Add(name);
			if (Islanders.Is(objectName)) { f.ReceiverCount++; f.Receivers.Add(IslandQuest.AsTarget(Islanders.NameOf(p))); }
			else if (ObjectProps.IsLoot(objectName, p))
			{
				f.ReceiverCount++;
				string t = ObjectProps.Get(p, ObjectProps.NoteTitle).Trim();
				if (t.Length > 0) f.Receivers.Add(IslandQuest.AsTarget(t));
			}
		}

		/// <summary>The island's own actions' signals (its settings' "on.arrive"...).</summary>
		public static void AddIslandFacts(PlanFacts f, IDictionary<string, string> islandProps)
		{
			foreach (string s in SignalsOf("", islandProps)) f.Signals.Add(s);
		}

		/// <summary>What is wrong with a step of these kinds (null = fine), and whether it is only a warning.</summary>
		public static string StepProblem(PlanFacts f, string type, string target, int count, out bool warning)
		{
			warning = false;
			target = (target ?? "").Trim();
			switch (type)
			{
				case Signal:
					string obj, state;
					if (target.Length == 0) return "it names no signal or object (\"lever\", \"Gate=open\")";
					if (IsState(target, out obj, out state))
						return f.Objects.Contains(obj) ? null : "no object is named '" + obj + "' (Behaviour: its name)";
					if (target.IndexOf('=') > 0) return "'" + target + "' is no state: write Name=open, closed, shown or hidden";
					warning = true;
					return f.Signals.Contains(target) ? null : "nothing on the island sends the signal '" + target + "' (a lever sends \"lever\", an action \"signal\"): it works only if another island or the world plan sends it there";
				case Bring:
					string item, to;
					SplitBring(target, out item, out to);
					if (item.Length == 0) return "it names no item to bring";
					if (ItemManager.GetAllItems() != null && ItemManager.GetAllItems().Count > 0 && ItemManager.GetItemByName(item) == null) return "Raft has no item '" + item + "'";
					if (f.ReceiverCount == 0) return "there is no islander or chest to hand the items to";
					if (to.Length > 0 && !f.Receivers.Contains(to)) return "no islander or chest is called '" + to + "'";
					break;
				case Craft:
				case Gather:
					if (target.Length > 0 && ItemManager.GetAllItems() != null && ItemManager.GetAllItems().Count > 0 && ItemManager.GetItemByName(target) == null) return "Raft has no item '" + target + "'";
					break;
				case Fish:
					if (target.Length == 0) break;
					if (ItemManager.GetAllItems() != null && ItemManager.GetAllItems().Count > 0 && ItemManager.GetItemByName(target) == null) return "Raft has no item '" + target + "'";
					warning = true;
					if (!FishNames().Contains(target, StringComparer.OrdinalIgnoreCase)) return "Raft's rod doesn't catch '" + target + "' (▾ lists the fish): the step may never be done";
					break;
			}
			return null;
		}

		#endregion
	}

	/// <summary>"Craft or cook" steps: an item crafted at the crafting menu (QuestSteps.Made).</summary>
	[HarmonyPatch(typeof(CraftingMenu), "CraftItem")]
	static class QuestStepsCraft
	{
		static void Prefix(CraftingMenu __instance, out Item_Base __state)
		{
			__state = null;
			try
			{
				SelectedRecipeBox box = Traverse.Create(__instance).Field("selectedRecipeBox").GetValue<SelectedRecipeBox>();
				__state = box != null ? box.ItemToCraft : null;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest craft] " + e.Message); }
		}

		static void Postfix(Item_Base __state)
		{
			try
			{
				if (__state != null && __state.settings_recipe != null) QuestSteps.Made(__state.UniqueName, Mathf.Max(1, __state.settings_recipe.AmountToCraft));
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest craft] " + e.Message); }
		}
	}

	/// <summary>"Craft or cook" steps: food or water taken off a grill, smelter or purifier by this player.</summary>
	[HarmonyPatch(typeof(Block_CookingStand), "CollectItem")]
	static class QuestStepsCook
	{
		static void Prefix(CookingSlot slot, Network_Player player, out Cost __state)
		{
			__state = null;
			try
			{
				if (player != null && player.IsLocalPlayer && slot != null && slot.CurrentItem != null && slot.CurrentItem.settings_cookable != null)
					__state = slot.CurrentItem.settings_cookable.CookingResult;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest cook] " + e.Message); }
		}

		static void Postfix(bool __result, Cost __state)
		{
			try { if (__result && __state != null && __state.item != null) QuestSteps.Made(__state.item.UniqueName, Mathf.Max(1, __state.amount)); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest cook] " + e.Message); }
		}
	}

	/// <summary>"Craft or cook" steps: a portion taken from a cooking pot or juicer by this player.</summary>
	[HarmonyPatch(typeof(CookingTable), "PickupFood")]
	static class QuestStepsPot
	{
		static void Prefix(CookingTable __instance, Network_Player player, out Item_Base __state)
		{
			__state = null;
			try
			{
				if (player != null && player.IsLocalPlayer && __instance.CurrentRecipe != null) __state = __instance.CurrentRecipe.Result;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest cook] " + e.Message); }
		}

		static void Postfix(bool __result, Item_Base __state)
		{
			try { if (__result && __state != null) QuestSteps.Made(__state.UniqueName, 1); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest cook] " + e.Message); }
		}
	}

	/// <summary>"Catch fish" steps: the fish this player's rod pulls out (the item Raft's bait pool gave: QuestSteps.Caught).</summary>
	[HarmonyPatch(typeof(FishingRod), "PullItemsFromSea")]
	static class QuestStepsFish
	{
		internal static bool pulling;
		internal static Item_Base pulled;

		static void Prefix() { pulling = true; pulled = null; }

		static void Postfix(FishingRod __instance)
		{
			Item_Base got = pulled;
			pulling = false;
			pulled = null;
			try
			{
				Network_Player p = Traverse.Create(__instance).Field("playerNetwork").GetValue<Network_Player>();
				for (Transform t = __instance.transform; p == null && t != null; t = t.parent) p = t.GetComponent<Network_Player>();
				if (got != null && p != null && p.IsLocalPlayer) QuestSteps.Caught(got.UniqueName, 1);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest fish] " + e.Message); }
		}
	}

	/// <summary>"Catch fish" steps: which item the bait pool gave while a rod is pulled in.</summary>
	[HarmonyPatch(typeof(FishingBaitHandler), "GetRandomItemFromCurrentBaitPool")]
	static class QuestStepsFishPool
	{
		static void Postfix(Item_Base __result) { if (QuestStepsFish.pulling && __result != null) QuestStepsFish.pulled = __result; }
	}

	/// <summary>
	/// "Gather" steps: what came into this player's inventory by a pickup (hand, shovel), a channelled pickup or a tree or
	/// bush harvested with the axe - the counts before and after, only while an island near the player waits for a gather
	/// step. The outermost hook counts (a harvest may go through a pickup).
	/// </summary>
	static class QuestStepsGather
	{
		static int depth;
		static Dictionary<string, int> before;

		internal static Dictionary<string, int> Counts(PlayerInventory inv)
		{
			var d = new Dictionary<string, int>();
			if (inv == null || inv.allSlots == null) return d;
			foreach (Slot s in inv.allSlots)
			{
				if (s == null || s.itemInstance == null || s.itemInstance.baseItem == null) continue;
				string n = s.itemInstance.UniqueName;
				int had;
				d.TryGetValue(n, out had);
				d[n] = had + s.itemInstance.Amount;
			}
			return d;
		}

		static PlayerInventory Local(Network_Player p) { return p != null && p.IsLocalPlayer ? p.Inventory : null; }

		internal static void Before(Network_Player p)
		{
			depth++;
			if (depth != 1) return;
			before = null;
			try
			{
				PlayerInventory inv = Local(p);
				if (inv != null && QuestSteps.GatherWaiting()) before = Counts(inv);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest gather] " + e.Message); }
		}

		internal static void After(Network_Player p)
		{
			depth = Math.Max(0, depth - 1);
			if (depth != 0 || before == null) return;
			Dictionary<string, int> had = before;
			before = null;
			PlayerInventory inv = Local(p);
			if (inv == null) return;
			foreach (var kv in Counts(inv))
			{
				int was;
				had.TryGetValue(kv.Key, out was);
				if (kv.Value > was) QuestSteps.Gathered(kv.Key, kv.Value - was);
			}
		}

		internal static Network_Player OwnerOf(PlayerInventory inv)
		{
			Network_Player p = RAPI.GetLocalPlayer();
			return p != null && inv != null && p.Inventory == inv ? p : null;
		}
	}

	[HarmonyPatch(typeof(Pickup), "AddItemToInventory")]
	static class QuestStepsPickup
	{
		static void Prefix(Pickup __instance) { Network_Player p = null; try { p = Traverse.Create(__instance).Field("playerNetwork").GetValue<Network_Player>(); } catch { } QuestStepsGather.Before(p); }
		static void Postfix(Pickup __instance) { Network_Player p = null; try { p = Traverse.Create(__instance).Field("playerNetwork").GetValue<Network_Player>(); } catch { } try { QuestStepsGather.After(p); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest gather] " + e.Message); } }
	}

	[HarmonyPatch(typeof(PickupChanneling), "AddItemToInventory")]
	static class QuestStepsChannel
	{
		static void Prefix(PickupChanneling __instance) { Network_Player p = null; try { p = Traverse.Create(__instance).Field("player").GetValue<Network_Player>(); } catch { } QuestStepsGather.Before(p); }
		static void Postfix(PickupChanneling __instance) { Network_Player p = null; try { p = Traverse.Create(__instance).Field("player").GetValue<Network_Player>(); } catch { } try { QuestStepsGather.After(p); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest gather] " + e.Message); } }
	}

	[HarmonyPatch(typeof(HarvestableTree), "Harvest")]
	static class QuestStepsHarvest
	{
		static void Prefix(PlayerInventory __0) { QuestStepsGather.Before(QuestStepsGather.OwnerOf(__0)); }
		static void Postfix(PlayerInventory __0) { try { QuestStepsGather.After(QuestStepsGather.OwnerOf(__0)); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [quest gather] " + e.Message); } }
	}
}
