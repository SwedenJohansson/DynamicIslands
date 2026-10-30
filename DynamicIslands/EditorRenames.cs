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
	/// zone's id: "reach" steps. The quest change joins the rename's own undo step, so one Ctrl+Z undoes both. Only when no
	/// other object keeps the old name (then the step may well mean that one).
	/// </summary>
	public static class EditorRenames
	{
		/// <summary>Quest steps of the given types whose target is <paramref name="from"/> (any case) point at
		/// <paramref name="to"/> instead, in every quest of an island's settings. The number of steps changed.</summary>
		public static int QuestTargets(IDictionary<string, string> props, string[] types, string from, string to)
		{
			from = (from ?? "").Trim(); to = (to ?? "").Trim();
			if (props == null || types == null || from.Length == 0 || to.Length == 0 || from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 0;
			// ("|" and line breaks would split the step)
			if (to.IndexOf('|') >= 0 || to.IndexOf('\n') >= 0) return 0;
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
			if (ObjectProps.IsNote(target.GameObjectName, target.Props) && !others.Any(e => ObjectProps.IsNote(e.GameObjectName, e.Props) && keeps(e))) types.Add("read");
			if (ObjectProps.IsLoot(target.GameObjectName, target.Props) && !others.Any(e => ObjectProps.IsLoot(e.GameObjectName, e.Props) && keeps(e))) types.Add("open");
			Island(types.ToArray(), oldTitle, newTitle);
		}

		/// <summary>After a zone's id changed (already an undo step): "reach" steps follow.</summary>
		public static void ZoneId(EditorGameObject target, string oldId, string newId)
		{
			if (target == null) return;
			oldId = (oldId ?? "").Trim();
			if (oldId.Length == 0) return;
			if (Others(target).Any(e => ObjectProps.Get(e.Props, ObjectProps.ZoneId, "").Trim().Equals(oldId, StringComparison.OrdinalIgnoreCase))) return;
			Island(new[] { "reach" }, oldId, newId);
		}

		static List<EditorGameObject> Others(EditorGameObject target)
		{
			GameObject placed = GameObject.Find("PlacedObjects");
			return placed == null ? new List<EditorGameObject>() : placed.GetComponentsInChildren<EditorGameObject>().Where(e => e != target && e.Props != null).ToList();
		}

		static void Island(string[] types, string from, string to)
		{
			if (types.Length == 0 || DynamicIslands.currentIslandProps == null) return;
			int count = 0;
			ICommand step = IslandSettingsUndo.Record(() => count = QuestTargets(DynamicIslands.currentIslandProps, types, from, to));
			if (step == null || count == 0) return;
			UndoRedoManager.AppendToLast(step);
			DynamicIslands.Notify(count + " quest step(s) now point at \"" + to.Trim() + "\"");
		}
	}
}
