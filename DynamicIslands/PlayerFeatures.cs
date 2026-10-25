using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The hub of the story features players meet on islands (2026-10-25): islanders to talk to (PF2, Islanders.cs).
	/// Behaviours, quests, the editors and the plan checker call in here, so each feature's own file keeps the rest.
	/// </summary>
	public static class PlayerFeatures
	{
		/// <summary>Verbs, checks and quest steps of these features (also in Behaviours' and Quest's lists).</summary>
		public const string DialogueVerb = "dialogue", TalkedCheck = "talked";

		/// <summary>ObjAction.HasTarget / HasArg for these verbs.</summary>
		public static bool HasTarget(string verb) { return verb == DialogueVerb; }
		public static bool HasArg(string verb) { return verb == DialogueVerb; }

		/// <summary>What a placed object starts with (ObjectProps.Defaults).</summary>
		public static void Defaults(string name, Dictionary<string, string> props)
		{
			Islanders.Defaults(name, props);
		}

		/// <summary>The catalog's hint for one of these objects, or null.</summary>
		public static string Hint(string name)
		{
			if (Islanders.Is(name)) return Islanders.Hint;
			return null;
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
		}

		/// <summary>Whether an event name belongs to these features (for the behaviour window's lists).</summary>
		public static bool IsEvent(string ev)
		{
			return ev == Islanders.TalkEvent || (ev != null && ev.StartsWith(Islanders.ChoicePrefix) && ev.Length == Islanders.ChoicePrefix.Length + 1);
		}

		/// <summary>World: the parts of these features an object gets (Behaviours.Attach).</summary>
		internal static void Attach(GameObject go, IslandObjectRef r)
		{
			try
			{
				if (Islanders.Is(r.ObjectName)) Islanders.Attach(go, r);
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] '" + r.ObjectName + "': " + ex.Message); }
		}

		/// <summary>An action's words for the builder, or null when it isn't one of these.</summary>
		internal static string Describe(ObjAction a)
		{
			switch (a.Verb)
			{
				case DialogueVerb: return "show a dialogue" + (a.Target.Length > 0 ? " said by '" + a.Target + "'" : "") + ": \"" + Short(a.Arg) + "\"";
			}
			return null;
		}

		/// <summary>A check's words for the builder, or null when it isn't one of these.</summary>
		internal static string What(ObjCheck c)
		{
			switch (c.Kind)
			{
				case TalkedCheck: return "someone talked to " + (c.Target.Length > 0 ? "'" + c.Target + "'" : "this islander");
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

		/// <summary>A quest step's words (Step.Describe), or null.</summary>
		internal static string DescribeStep(string type, string target, int count)
		{
			switch (type)
			{
				case Islanders.QuestType: return "Talk to " + (target.Length > 0 ? target : "an islander");
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
			}
			return null;
		}

		/// <summary>The behaviour window's placeholder for a check's name field, or null when it isn't one of these.</summary>
		internal static string CheckPlaceholder(string kind)
		{
			switch (kind)
			{
				case TalkedCheck: return "islander name (empty = this one)";
			}
			return null;
		}

		internal static string CheckHint(string kind)
		{
			switch (kind)
			{
				case TalkedCheck: return "The name of an islander (empty = this one): true once anyone of the crew talked to it";
			}
			return "";
		}

		/// <summary>Facts a plan check knows of these features: islanders' names...</summary>
		public class PlanFacts
		{
			public readonly HashSet<string> Islanders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Collects one object's facts (PlanChecker.FromFile).</summary>
		public static void AddFacts(PlanFacts f, string objectName, IDictionary<string, string> p)
		{
			if (Islanders.Is(objectName)) f.Islanders.Add(Islanders.NameOf(p));
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
