using System;
using System.Collections.Generic;
using System.Linq;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Renames in the editor carry over to what points at the old name (player 10-30: after renaming a note its quest step
	/// still looked for the old title and never finished). A note's or chest's title: the quest's "read"/"open" steps; a
	/// zone's id: "reach" steps and the island's own rules that name it; an object's name (obj.name): the actions, checks
	/// and "talk" steps that name it. The quest change joins the rename's own undo step, so one Ctrl+Z undoes both. Only when no
	/// other object keeps the old name (then the step may well mean that one).
	/// </summary>
	public static class EditorRenames
	{
		/// <summary>Quest steps of the given types whose target is <paramref name="from"/> (any case) point at
		/// <paramref name="to"/> instead, in every quest of an island's settings. The number of steps changed.</summary>
		public static int QuestTargets(IDictionary<string, string> props, string[] types, string from, string to)
		{
			// ("|" and line breaks would split the step: both names as the step keeps them, "/" and a space - the game compares
			// that form, QuestTracker.Matches; a title with "|" was left out and its step looked for the old one - review 10-30)
			from = IslandQuest.Clean(from); to = IslandQuest.Clean(to);
			if (props == null || types == null || from.Length == 0 || to.Length == 0 || from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 0;
			int count = 0;
			for (int n = 0; n < IslandQuest.MaxQuests; n++)
			{
				string k = IslandQuest.Key(IslandQuest.KeySteps, n), steps;
				if (!props.TryGetValue(k, out steps) || string.IsNullOrEmpty(steps)) continue;
				string[] lines = steps.Split('\n');
				bool any = false;
				for (int i = 0; i < lines.Length; i++)
				{
					string[] p = lines[i].Split('|');
					if (p.Length < 2 || Array.IndexOf(types, p[0]) < 0 || !p[1].Trim().Equals(from, StringComparison.OrdinalIgnoreCase)) continue;
					p[1] = to;
					lines[i] = string.Join("|", p);
					any = true; count++;
				}
				if (any) props[k] = string.Join("\n", lines);
			}
			return count;
		}

		/// <summary>After a note's or chest's title changed (already an undo step): its quest steps follow.</summary>
		public static void NoteTitle(EditorGameObject target, string oldTitle, string newTitle)
		{
			if (target == null) return;
			oldTitle = (oldTitle ?? "").Trim(); newTitle = (newTitle ?? "").Trim();
			if (oldTitle.Length == 0 || newTitle.Length == 0 || oldTitle.Equals(newTitle, StringComparison.OrdinalIgnoreCase)) return;
			List<EditorGameObject> others = Others(target);
			Func<EditorGameObject, bool> keeps = e => ObjectProps.Get(e.Props, ObjectProps.NoteTitle, "").Trim().Equals(oldTitle, StringComparison.OrdinalIgnoreCase);
			var types = new List<string>();
			// (not when another note or chest has the new title already: the steps would then mean both - TellTaken)
			List<EditorGameObject> notes = others.Where(e => ObjectProps.IsNote(e.GameObjectName, e.Props)).ToList(), chests = others.Where(e => ObjectProps.IsLoot(e.GameObjectName, e.Props)).ToList();
			bool taken = false;
			if (ObjectProps.IsNote(target.GameObjectName, target.Props) && !notes.Any(keeps))
			{
				if (Taken(notes.Select(e => (IDictionary<string, string>)e.Props), ObjectProps.NoteTitle, newTitle)) taken = true; else types.Add("read");
			}
			if (ObjectProps.IsLoot(target.GameObjectName, target.Props) && !chests.Any(keeps))
			{
				if (Taken(chests.Select(e => (IDictionary<string, string>)e.Props), ObjectProps.NoteTitle, newTitle)) taken = true; else types.Add("open");
			}
			if (taken) TellTaken("title", newTitle);
			Island(types.ToArray(), oldTitle, newTitle, types.Contains("read") ? "note" : null);
		}

		/// <summary>Whether one of these objects' settings has this value (any case) under the key already.</summary>
		internal static bool Taken(IEnumerable<IDictionary<string, string>> others, string key, string value)
		{
			value = (value ?? "").Trim();
			return value.Length > 0 && others.Any(p => p != null && ObjectProps.Get(p, key, "").Trim().Equals(value, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>A rename to a title, zone id or name another object has already: what pointed at the old one is left as it
		/// was - moved, it would silently mean both objects (two notes' "read" steps merged into one).</summary>
		static void TellTaken(string what, string name)
		{
			DynamicIslands.Notify("Another object already has the " + what + " \"" + name.Trim() + "\" - the quest steps and actions that named the old " + what + " were left as they were (they would mean both). Check them.", true, 8);
		}

		/// <summary>After a zone's id changed (already an undo step): "reach" steps follow.</summary>
		public static void ZoneId(EditorGameObject target, string oldId, string newId)
		{
			if (target == null) return;
			oldId = (oldId ?? "").Trim();
			if (oldId.Length == 0) return;
			List<EditorGameObject> others = Others(target);
			if (others.Any(e => ObjectProps.Get(e.Props, ObjectProps.ZoneId, "").Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase))) return;
			List<IDictionary<string, string>> zones = others.Where(e => e.GameObjectName == ContentCatalog.TriggerZone).Select(e => (IDictionary<string, string>)e.Props).ToList();
			if (Taken(zones, ObjectProps.ZoneId, newId)) { TellTaken("zone id", newId); return; }
			Island(new[] { "reach" }, oldId, newId, "zone");
		}

		/// <summary>The other placed objects - hidden ones too (an object "Hidden until shown" may be inactive).</summary>
		static List<EditorGameObject> Others(EditorGameObject target)
		{
			GameObject placed = GameObject.Find("PlacedObjects");
			return placed == null ? new List<EditorGameObject>() : placed.GetComponentsInChildren<EditorGameObject>(true).Where(e => e != target && e.Props != null).ToList();
		}

		/// <summary>The quest steps of these types and the island's own rules (rule: "zone" or "note", or null) follow.</summary>
		static void Island(string[] types, string from, string to, string rule = null)
		{
			if ((types.Length == 0 && rule == null) || DynamicIslands.currentIslandProps == null) return;
			int count = 0, rules = 0;
			ICommand step = IslandSettingsUndo.Record(() =>
			{
				count = types.Length > 0 ? QuestTargets(DynamicIslands.currentIslandProps, types, from, to) : 0;
				rules = rule != null ? RuleRefs(DynamicIslands.currentIslandProps, rule, from, to, DynamicIslands.currentIslandName) : 0;
			});
			if (step == null || count + rules == 0) return;
			UndoRedoManager.AppendToLast(step);
			DynamicIslands.Notify((count > 0 ? count + " quest step(s)" : "") + (count > 0 && rules > 0 ? " and " : "") + (rules > 0 ? rules + " island rule(s)" : "") + " now point at \"" + to.Trim() + "\"");
		}

		/// <summary>Check kinds whose target is an object's name (obj.name).</summary>
		static readonly string[] NamedChecks = { "state", PlayerFeatures.TalkedCheck, Lights.LightCheck };

		/// <summary>One set of settings (an object's or the island's): actions that act on an object by its name (show, open,
		/// teleport...: ObjAction.HasTarget), checks of a named object (state, talked to, light) and a camera point's look-at
		/// that name <paramref name="from"/> (any case) name <paramref name="to"/> instead. The number changed.</summary>
		public static int NameRefs(IDictionary<string, string> props, string from, string to)
		{
			from = (from ?? "").Trim(); to = (to ?? "").Trim();
			if (props == null || from.Length == 0 || to.Length == 0 || from.Equals(to, StringComparison.OrdinalIgnoreCase) || to.IndexOf('|') >= 0 || to.IndexOf('\n') >= 0) return 0;
			int count = 0;
			foreach (string k in props.Keys.ToList())
			{
				string v = props[k] ?? "";
				if (k == CameraPath.LookProp)
				{
					if (v.Trim().Equals(from, StringComparison.OrdinalIgnoreCase)) { props[k] = to; count++; }
					continue;
				}
				bool actions = k.StartsWith(BehaviourProps.EventPrefix) || k.StartsWith(BehaviourProps.ElsePrefix);
				if (!actions && !k.StartsWith(BehaviourProps.CheckPrefix)) continue;
				string[] lines = v.Split('\n');
				bool any = false;
				for (int i = 0; i < lines.Length; i++)
				{
					string[] p = lines[i].Split('|');
					if (p.Length < 2 || !p[1].Trim().Equals(from, StringComparison.OrdinalIgnoreCase)) continue;
					string head = p[0].Trim();
					if (actions ? !ObjAction.HasTarget(head) : Array.IndexOf(NamedChecks, head.TrimStart('!')) < 0) continue;
					p[1] = to;
					lines[i] = string.Join("|", p);
					any = true; count++;
				}
				if (any) props[k] = string.Join("\n", lines);
			}
			return count;
		}

		/// <summary>After an object's name (obj.name: Behaviour &amp; events, an islander's name) changed (already an undo step):
		/// what acts on it or checks it by that name follows - every object's actions and checks, camera points looking at it,
		/// the island's events and, for an islander, the quest's "talk" steps - in the same undo step. Not while another object
		/// keeps the old name (objects sharing a name act together: the references still mean that one).</summary>
		public static void ObjectName(EditorGameObject target, string oldName, string newName)
		{
			if (target == null) return;
			oldName = (oldName ?? "").Trim(); newName = (newName ?? "").Trim();
			if (oldName.Length == 0 || newName.Length == 0 || oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)) return;
			List<EditorGameObject> others = Others(target);
			if (others.Any(e => ObjectProps.Get(e.Props, BehaviourProps.Name).Trim().Equals(oldName, StringComparison.OrdinalIgnoreCase))) return;
			if (Taken(others.Select(e => (IDictionary<string, string>)e.Props), BehaviourProps.Name, newName)) { TellTaken("name", newName); return; }
			int count = 0;
			string[] talk = Islanders.Is(target.GameObjectName) ? new[] { Islanders.QuestType } : new string[0];
			if (DynamicIslands.currentIslandProps != null)
			{
				ICommand step = IslandSettingsUndo.Record(() => count += NameRefs(DynamicIslands.currentIslandProps, oldName, newName)
					+ (talk.Length > 0 ? QuestTargets(DynamicIslands.currentIslandProps, talk, oldName, newName) : 0));
				if (step != null) UndoRedoManager.AppendToLast(step);
			}
			GameObject placed = GameObject.Find("PlacedObjects");
			if (placed != null)
				foreach (EditorGameObject e in placed.GetComponentsInChildren<EditorGameObject>(true))
				{
					if (e.Props == null) continue;
					var after = new Dictionary<string, string>(e.Props);
					int n = NameRefs(after, oldName, newName);
					if (n == 0) continue;
					var cmd = new PropsCommand(e, e.Props, after);
					cmd.Execute();
					UndoRedoManager.AppendToLast(cmd);
					count += n;
				}
			if (count > 0) DynamicIslands.Notify(count + " action(s), check(s) or step(s) now name \"" + newName + "\"");
		}

		/// <summary>The island's own world-plan rules (bring.rules) that name one of its zones or notes: "when a player reaches
		/// zone X" of this island (zone:self:X), and "done when" zone:X / note:X of a rule bringing this island itself. kind:
		/// "zone" or "note". Rules about other islands, and world plan files, are left alone. The number changed.</summary>
		public static int RuleRefs(IDictionary<string, string> props, string kind, string from, string to, string islandName)
		{
			from = (from ?? "").Trim(); to = (to ?? "").Trim();
			string text;
			if (props == null || from.Length == 0 || to.Length == 0 || from.Equals(to, StringComparison.OrdinalIgnoreCase) || to.IndexOf('|') >= 0 || to.IndexOf('\n') >= 0
				|| !props.TryGetValue(WorldDirector.IslandRulesKey, out text) || string.IsNullOrEmpty(text)) return 0;
			string me = (islandName ?? "").Trim();
			Func<string, bool> own = r => { r = (r ?? "").Trim(); return r.Length == 0 || r.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase) || (me.Length > 0 && r.Equals(me, StringComparison.OrdinalIgnoreCase)); };
			string[] lines = text.Split('\n');
			int count = 0;
			for (int i = 0; i < lines.Length; i++)
			{
				IntroRule r = IntroRule.Parse(lines[i]);
				if (r == null) continue;
				bool changed = false;
				if (kind == "zone" && r.When == "zone" && own(r.WhenRef) && r.WhenArg.Trim().Equals(from, StringComparison.OrdinalIgnoreCase)) { r.WhenArg = to; changed = true; }
				int c = r.StoryDone.IndexOf(':');
				if (c > 0 && r.StoryDone.Substring(0, c).Trim().Equals(kind, StringComparison.OrdinalIgnoreCase) && r.StoryDone.Substring(c + 1).Trim().Equals(from, StringComparison.OrdinalIgnoreCase)
					&& r.What == "island" && r.WhatArg.Trim().Length > 0 && own(r.WhatArg)) { r.StoryDone = kind + ":" + to; changed = true; }
				if (!changed) continue;
				lines[i] = r.ToLine();
				count++;
			}
			if (count > 0) props[WorldDirector.IslandRulesKey] = string.Join("\n", lines);
			return count;
		}
	}
}
