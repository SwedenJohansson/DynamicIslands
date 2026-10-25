using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>The inspector's rows for the story features players meet (2026-10-25, PlayerFeatures): an islander's
	/// character, pose and dialogue.</summary>
	public static partial class ObjectInspector
	{
		static void PlayerGroup(EditorGameObject target)
		{
			if (Islanders.Is(target.GameObjectName)) IslanderGroup(target);
		}

		static void IslanderGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			RectTransform g = UIKit.Group(root, "Islander");
			InputField name = SurvivorField(g, "Name", ObjectProps.Get(p, BehaviourProps.Name, "Islander"), 26f, "Who it is: the name in its dialogue, and what a quest's \"Talk to\" step and the \"talked to\" check use");
			name.characterLimit = 32;
			name.onEndEdit.AddListener(v => Set(target, BehaviourProps.Name, v.Trim().Length > 0 ? v.Trim() : "Islander", null));
			amountFields.Add(name);
			// Who it looks like: steps through Raft's characters (none: one picked by the island and the object)
			string ch = ObjectProps.Get(p, Islanders.CharacterProp);
			List<string> chars = new[] { "" }.Concat(Behaviours.Characters().Where(c => c != null).Select(c => c.displayName)).Distinct().ToList();
			if (ch.Length > 0 && !chars.Contains(ch)) chars.Add(ch);
			RectTransform crow = UIKit.Row(g, 26f, 4f, "IslanderCharacter");
			UIKit.Size(UIKit.Label(crow, "Looks like", 13, UIKit.TextMuted).gameObject, 80);
			UIKit.Button(crow, ch.Length > 0 ? ch : "any (by its place)", () =>
			{
				string next = chars[(chars.IndexOf(ch) + 1) % chars.Count];
				Set(target, Islanders.CharacterProp, next.Length > 0 ? next : null);
				Refresh();
			}, "Click to step through Raft's characters", -1, 26f, 12);
			// Its pose: one button each
			string pose = Islanders.PoseOf(p);
			RectTransform prow = UIKit.Row(g, 26f, 3f, "IslanderPose");
			UIKit.Size(UIKit.Label(prow, "Pose", 13, UIKit.TextMuted).gameObject, 80);
			foreach (string ps in Islanders.Poses)
			{
				string chosen = ps;
				Button b = UIKit.Button(prow, ps, () => { Set(target, Islanders.PoseProp, chosen); Refresh(); }, "The islander " + (ps == "stand" ? "stands" : ps == "sit" ? "sits" : ps == "fish" ? "fishes with a rod" : ps == "sleep" ? "lies asleep" : "leans") + " (place it where that fits: on a seat, at the water, against a wall)", -1, 26f, 11);
				if (ps == pose) UIKit.Primary(b);
			}
			UIKit.Size(UIKit.Label(g, "What it says (--- on a line: next page; > at a line's start: an answer)", 12, UIKit.TextMuted).gameObject, -1, 16);
			InputField text = UIKit.TextArea(g, "Hello there.\n---\nWill you help me?\n> Yes\n> Not now", 110f, "The dialogue players get with the interact key. Answer 1..3 fires the islander's choice1..choice3 event; closing it fires \"talk\"");
			text.text = ObjectProps.Get(p, Islanders.TextProp);
			text.characterLimit = 2000;
			text.onEndEdit.AddListener(v => Set(target, Islanders.TextProp, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(text);
			int pages = Islanders.Parse(text.text).Count, answers = Islanders.Choices(text.text).Count;
			UIKit.Size(UIKit.Label(g, pages + " page" + (pages == 1 ? "" : "s") + (answers > 0 ? ", " + answers + " answer" + (answers == 1 ? "" : "s") : "") + ". Events: Behaviour > \"talk\", \"choice1\"..", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic).gameObject, -1, 16);
		}
	}
}
