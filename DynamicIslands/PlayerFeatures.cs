using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The hub of the story features players meet on islands (2026-10-25): islanders to talk to (PF2, Islanders.cs), hidden
	/// collectible sets (PF3, Collectibles.cs).
	/// Behaviours, quests, the editors and the plan checker call in here, so each feature's own file keeps the rest.
	/// </summary>
	public static class PlayerFeatures
	{
		/// <summary>Verbs, checks and quest steps of these features (also in Behaviours' and Quest's lists).</summary>
		public const string DialogueVerb = "dialogue", TalkedCheck = "talked";

		/// <summary>ObjAction.HasTarget / HasArg for these verbs.</summary>
		public static bool HasTarget(string verb) { return verb == DialogueVerb || verb == Lights.Verb; }
		public static bool HasArg(string verb) { return verb == DialogueVerb || verb == Lights.Verb; }

		/// <summary>What a placed object starts with (ObjectProps.Defaults).</summary>
		public static void Defaults(string name, Dictionary<string, string> props)
		{
			Islanders.Defaults(name, props);
			Collectibles.Defaults(name, props);
			Lights.Defaults(name, props);
		}

		/// <summary>The catalog's hint for one of these objects, or null.</summary>
		public static string Hint(string name)
		{
			if (Islanders.Is(name)) return Islanders.Hint;
			if (Collectibles.Is(name)) return Collectibles.Hint;
			if (Lights.Is(name)) return Lights.Hint;
			return null;
		}

		/// <summary>World: an object's lines as if the builder had written them (a set piece is picked up when used).</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			return Lights.Expand(objectName, Collectibles.Expand(objectName, props));
		}

		/// <summary>Host, from the mod's update: sets found (Collectibles).</summary>
		internal static void Tick()
		{
			Collectibles.Tick();
		}

		/// <summary>The events one of these objects has (Behaviours.EventsFor).</summary>
		public static void Events(List<KeyValuePair<string, string>> list, string objectName, IDictionary<string, string> p)
		{
			if (Islanders.Is(objectName))
			{
				// (an islander is talked to, not used)
				list.RemoveAll(x => x.Key == "use");
				list.Add(new KeyValuePair<string, string>(Islanders.TalkEvent, "a player talked to it (its dialogue closed)"));
				List<string> answers = Islanders.Choices(ObjectProps.Get(p, Islanders.TextProp));
				for (int i = 0; i < Islanders.MaxChoices; i++)
					list.Add(new KeyValuePair<string, string>(Islanders.ChoicePrefix + (i + 1), "a player picked answer " + (i + 1) + (i < answers.Count ? " (\"" + answers[i] + "\")" : " (its dialogue has no answer " + (i + 1) + " yet: a line \"> ...\")")));
			}
			if (Collectibles.Is(objectName) || Collectibles.IsPiece(p))
				list.Add(new KeyValuePair<string, string>(Collectibles.SetEvent, "the last piece of its set on the island was found (runs on every piece of the set, once: the set's reward)"));
			if (Lights.IsLight(objectName, p))
			{
				list.Add(new KeyValuePair<string, string>(Lights.LitEvent, "it was switched on"));
				list.Add(new KeyValuePair<string, string>(Lights.UnlitEvent, "it was switched off"));
				list.Add(new KeyValuePair<string, string>(Lights.AllLitEvent, "every light of its group is on now (runs on each of them)"));
			}
		}

		/// <summary>Whether an event name belongs to these features (for the behaviour window's lists).</summary>
		public static bool IsEvent(string ev)
		{
			return ev == Islanders.TalkEvent || ev == Collectibles.SetEvent || ev == Lights.LitEvent || ev == Lights.UnlitEvent || ev == Lights.AllLitEvent || (ev != null && ev.StartsWith(Islanders.ChoicePrefix) && ev.Length == Islanders.ChoicePrefix.Length + 1);
		}

		/// <summary>World: the parts of these features an object gets (Behaviours.Attach).</summary>
		internal static void Attach(GameObject go, IslandObjectRef r)
		{
			try
			{
				if (Islanders.Is(r.ObjectName)) Islanders.Attach(go, r);
				if (Collectibles.IsPiece(r.Props)) Collectibles.Attach(go, r);
				if (Lights.IsLight(r.ObjectName, r.Props)) Lights.Attach(go, r);
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] '" + r.ObjectName + "': " + ex.Message); }
		}

		/// <summary>An action's words for the builder, or null when it isn't one of these.</summary>
		internal static string Describe(ObjAction a)
		{
			switch (a.Verb)
			{
				case DialogueVerb: return "show a dialogue" + (a.Target.Length > 0 ? " said by '" + a.Target + "'" : "") + ": \"" + Short(a.Arg) + "\"";
				case Lights.Verb: return "switch " + ((a.Arg ?? "").Trim().Length > 0 ? a.Arg.Trim() : "over") + " " + (a.Target.Trim().Length == 0 ? "this light" : a.Target.Trim().StartsWith(Lights.GroupPrefix) ? "the lights of '" + a.Target.Trim().Substring(Lights.GroupPrefix.Length) + "'" : "'" + a.Target + "'");
			}
			return null;
		}

		/// <summary>A check's words for the builder, or null when it isn't one of these.</summary>
		internal static string What(ObjCheck c)
		{
			switch (c.Kind)
			{
				case TalkedCheck: return "someone talked to " + (c.Target.Length > 0 ? "'" + c.Target + "'" : "this islander");
				case Collectibles.CheckKind: return (c.Arg.Length > 0 ? c.Arg : "all") + " of the set '" + c.Target + "' found";
				case Lights.LightCheck: return (c.Target.Length > 0 ? "'" + c.Target + "'" : "this light") + " is " + (c.Arg.Trim().ToLowerInvariant() == "off" ? "off" : "on");
				case Lights.GroupCheck: return "all lights of '" + c.Target + "' are " + (c.Arg.Trim().ToLowerInvariant() == "off" ? "off" : "on");
			}
			return null;
		}

		/// <summary>Whether a check holds (Behaviours.Holds); false for a kind that isn't one of these.</summary>
		internal static bool Holds(IslandWorldState.Entry e, int index, ObjCheck c, out bool result)
		{
			result = false;
			switch (c.Kind)
			{
				case TalkedCheck: result = Islanders.TalkedTo(e, index, c.Target); return true;
				case Collectibles.CheckKind: result = Collectibles.Holds(e, c.Target, c.Arg); return true;
				case Lights.LightCheck:
				case Lights.GroupCheck: result = Lights.Holds(e, index, c); return true;
			}
			return false;
		}

		/// <summary>A personal action of these features (Behaviours.RunPersonal); false when it isn't one.</summary>
		internal static bool RunPersonal(IslandWorldState.Entry e, int index, ObjAction a)
		{
			switch (a.Verb)
			{
				case DialogueVerb: Islanders.ShowAction(e, index, a); return true;
			}
			return false;
		}

		/// <summary>Host: a shared action of these features (Behaviours.RunShared); false when it isn't one.</summary>
		internal static bool RunShared(IslandWorldState.Entry e, int index, ObjAction a)
		{
			switch (a.Verb)
			{
				case Lights.Verb: Lights.HostRun(e, index, a); return true;
			}
			return false;
		}

		/// <summary>A quest step's words (Step.Describe), or null.</summary>
		internal static string DescribeStep(string type, string target, int count)
		{
			switch (type)
			{
				case Islanders.QuestType: return "Talk to " + (target.Length > 0 ? target : "an islander");
				case Lights.QuestType: return "Light " + count + " of the lights of " + (target.Length > 0 ? target : "a group");
				case Collectibles.QuestType: return "Find " + count + " of the " + (target.Length > 0 ? target : "set");
			}
			return null;
		}

		/// <summary>The names the quest editor's ▾ offers for a step, or null.</summary>
		internal static List<DropList.Option> NamesFor(string type, IEnumerable<EditorGameObject> all)
		{
			switch (type)
			{
				case Islanders.QuestType:
					return all.Where(g => Islanders.Is(g.GameObjectName)).Select(g => Islanders.NameOf(g.Props)).Distinct()
						.Select(n => new DropList.Option(n, n, "An islander on this island")).ToList();
				case Collectibles.QuestType:
					return all.Select(g => Collectibles.SetOf(g.Props)).Where(n => n.Length > 0).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
						.Select(n => new DropList.Option(n.Key, n.Key, n.Count() + " piece(s) on this island")).ToList();
				case Lights.QuestType:
					return all.Where(g => Lights.IsLight(g.GameObjectName, g.Props)).Select(g => Lights.GroupOf(g.Props)).Where(n => n.Length > 0).GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
						.Select(n => new DropList.Option(n.Key, n.Key, n.Count() + " light(s) on this island")).ToList();
			}
			return null;
		}

		/// <summary>The behaviour window's placeholder for a check's name field, or null when it isn't one of these.</summary>
		internal static string CheckPlaceholder(string kind)
		{
			switch (kind)
			{
				case TalkedCheck: return "islander name (empty = this one)";
				case Collectibles.CheckKind: return "set name";
				case Lights.LightCheck: return "light name (empty = this one)";
				case Lights.GroupCheck: return "light group";
			}
			return null;
		}

		internal static string CheckHint(string kind)
		{
			switch (kind)
			{
				case TalkedCheck: return "The name of an islander (empty = this one): true once anyone of the crew talked to it";
				case Collectibles.CheckKind: return "A collectible set's name (the pieces' Set): true once that many of its pieces on this island were found";
				case Lights.LightCheck: return "A light's name (empty = this one; group:G = every light of group G): true while every such light is on (or off)";
				case Lights.GroupCheck: return "A light group's name: true while every light of the group on this island is on (or off)";
			}
			return "";
		}

		/// <summary>The behaviour window's second field of a check (its Arg), or null when it has none.</summary>
		internal static string CheckArgPlaceholder(string kind)
		{
			switch (kind)
			{
				case Collectibles.CheckKind: return "how many (empty = all)";
				case Lights.LightCheck:
				case Lights.GroupCheck: return "on or off";
			}
			return null;
		}

		internal static string CheckArgHint(string kind)
		{
			switch (kind)
			{
				case Collectibles.CheckKind: return "How many pieces must be found (empty = every piece of the set on this island)";
				case Lights.LightCheck:
				case Lights.GroupCheck: return "on (empty) or off";
			}
			return "";
		}

		/// <summary>Quest steps the host counts (IslandQuest.Counted) and how far they are (QuestTracker.Found).</summary>
		public static bool Counted(string type) { return type == Collectibles.QuestType || type == Lights.QuestType; }

		public static int Found(IslandWorldState.Entry e, IslandQuest.Step s)
		{
			if (s.Type == Collectibles.QuestType) return Collectibles.Found(e, s.Target);
			if (s.Type == Lights.QuestType) return Lights.LitCount(e, s.Target);
			return 0;
		}

		/// <summary>Facts a plan check knows of these features: islanders' names...</summary>
		public class PlanFacts
		{
			public readonly HashSet<string> Islanders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			/// <summary>Collectible sets: name, pieces.</summary>
			public readonly Dictionary<string, int> Sets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			/// <summary>Light groups: name, lights.</summary>
			public readonly Dictionary<string, int> LightGroups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Collects one object's facts (PlanChecker.FromFile).</summary>
		public static void AddFacts(PlanFacts f, string objectName, IDictionary<string, string> p)
		{
			if (Islanders.Is(objectName)) f.Islanders.Add(Islanders.NameOf(p));
			string set = Collectibles.SetOf(p);
			if (set.Length > 0) { int n; f.Sets.TryGetValue(set, out n); f.Sets[set] = n + 1; }
			string lg = Lights.IsLight(objectName, p) ? Lights.GroupOf(p) : "";
			if (lg.Length > 0) { int n; f.LightGroups.TryGetValue(lg, out n); f.LightGroups[lg] = n + 1; }
		}

		/// <summary>What is wrong with a quest step of these features (null = fine; plan check, quest editor).</summary>
		public static string StepProblem(PlanFacts f, string type, string target, int count)
		{
			switch (type)
			{
				case Islanders.QuestType:
					if (f.Islanders.Count == 0) return "there is no islander (Story_Islander) to talk to";
					if (target.Length > 0 && !f.Islanders.Contains(target)) return "no islander is called '" + target + "' (" + string.Join(", ", f.Islanders.ToArray()) + ")";
					break;
				case Collectibles.QuestType:
					int pieces;
					if (target.Length == 0) return "it names no set";
					if (!f.Sets.TryGetValue(target, out pieces)) return "no object is a piece of the set '" + target + "'" + (f.Sets.Count > 0 ? " (" + string.Join(", ", f.Sets.Keys.ToArray()) + ")" : " (Collectible (set piece), Loot & chests)");
					if (count > pieces) return "it wants " + count + " of the set '" + target + "', the island has " + pieces;
					break;
				case Lights.QuestType:
					int lamps;
					if (target.Length == 0) return "it names no light group";
					if (!f.LightGroups.TryGetValue(target, out lamps)) return "no light is in the group '" + target + "'" + (f.LightGroups.Count > 0 ? " (" + string.Join(", ", f.LightGroups.Keys.ToArray()) + ")" : " (Puzzle light (switch), Zones & triggers)");
					if (count > lamps) return "it wants " + count + " lights of '" + target + "' on, the group has " + lamps;
					break;
			}
			return null;
		}

		static string Short(string s)
		{
			s = (s ?? "").Replace("\\n", " ").Replace("\n", " ").Trim();
			return s.Length > 50 ? s.Substring(0, 47) + "..." : s;
		}
	}
}
