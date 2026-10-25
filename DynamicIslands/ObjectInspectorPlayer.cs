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
			if (Collectibles.Is(target.GameObjectName) || Collectibles.IsPiece(target.Props)) CollectibleGroup(target);
			if (Lights.IsLight(target.GameObjectName, target.Props)) LightGroup(target);
			MakeFeatureGroup(target);
		}

		/// <summary>Any object: buttons that make it a piece of a collectible set or a puzzle light (the prop set for the
		/// builder; its group then shows). Not on islanders.</summary>
		static void MakeFeatureGroup(EditorGameObject target)
		{
			if (Islanders.Is(target.GameObjectName)) return;
			bool piece = Collectibles.IsPiece(target.Props), light = Lights.IsLight(target.GameObjectName, target.Props);
			if (piece && light) return;
			RectTransform g = UIKit.Group(root, "Story puzzle");
			RectTransform row = UIKit.Row(g, UIKit.RowHeight, 4f, "MakeStoryFeature");
			if (!piece)
				UIKit.Button(row, "Make collectible (set)", () => { Set(target, Collectibles.SetProp, "Relics"); Refresh(); }, "Makes this object a hidden piece of a collectible set (Relics; change it below): players pick it up with E, the HUD counts the set, the whole set runs the \"set\" event");
			if (!light)
				UIKit.Button(row, "Make puzzle light (group)", () => { Set(target, Lights.GroupProp, "Lamps"); Refresh(); }, "Makes this object a puzzle light of a group (Lamps; change it below): players switch it with E; events lit, unlit, alllit");
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

		/// <summary>A piece of a collectible set (PF3): its set's name and how many pieces the island has.</summary>
		static void CollectibleGroup(EditorGameObject target)
		{
			RectTransform g = UIKit.Group(root, "Collectible set");
			string set = Collectibles.SetOf(target.Props);
			InputField f = SurvivorField(g, "Set", set, 26f, "The set this piece belongs to (any object with a set is a piece; empty = none). Players pick pieces up; the HUD counts them; the whole set runs every piece's \"set\" event");
			f.characterLimit = 32;
			f.onEndEdit.AddListener(v => { Set(target, Collectibles.SetProp, v.Trim().Length > 0 ? v.Trim() : null); Refresh(); });
			amountFields.Add(f);
			GameObject placed = GameObject.Find("PlacedObjects");
			int pieces = placed == null || set.Length == 0 ? 0 : placed.GetComponentsInChildren<EditorGameObject>().Count(o => string.Equals(Collectibles.SetOf(o.Props), set, System.StringComparison.OrdinalIgnoreCase));
			UIKit.Size(UIKit.Label(g, set.Length == 0 ? "No set: not a piece." : pieces + " piece" + (pieces == 1 ? "" : "s") + " of \"" + set + "\" on this island. Reward: Behaviour > \"set\"; quest step \"Find set\"; check \"set found\"", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic).gameObject, -1, 16);
		}

		/// <summary>A puzzle light (PF6): its group, whether it starts on, and how many lights its group has.</summary>
		static void LightGroup(EditorGameObject target)
		{
			RectTransform g = UIKit.Group(root, "Puzzle light");
			string group = Lights.GroupOf(target.Props);
			InputField f = SurvivorField(g, "Group", group, 26f, "The lights' group: when every light of it is on, each runs its \"alllit\" event and the island sends the signal lights:<group> (empty = no group; any object with a group is a light)");
			f.characterLimit = 32;
			f.onEndEdit.AddListener(v => { Set(target, Lights.GroupProp, v.Trim().Length > 0 ? v.Trim() : null); Refresh(); });
			amountFields.Add(f);
			bool on = Lights.StartsOn(target.Props);
			RectTransform row = UIKit.Row(g, 26f, 4f, "LightStartsOn");
			UIKit.Size(UIKit.Label(row, "Starts", 13, UIKit.TextMuted).gameObject, 80);
			Button offB = UIKit.Button(row, "off", () => { Set(target, Lights.OnProp, null); Refresh(); }, "The light starts switched off", -1, 26f, 12);
			Button onB = UIKit.Button(row, "on", () => { Set(target, Lights.OnProp, "1"); Refresh(); }, "The light starts switched on (players switch it off)", -1, 26f, 12);
			UIKit.Primary(on ? onB : offB);
			GameObject placed = GameObject.Find("PlacedObjects");
			int lights = placed == null || group.Length == 0 ? 0 : placed.GetComponentsInChildren<EditorGameObject>().Count(o => Lights.IsLight(o.GameObjectName, o.Props) && string.Equals(Lights.GroupOf(o.Props), group, System.StringComparison.OrdinalIgnoreCase));
			UIKit.Size(UIKit.Label(g, (group.Length == 0 ? "No group." : lights + " light" + (lights == 1 ? "" : "s") + " in \"" + group + "\" on this island.") + " Events: \"lit\", \"unlit\", \"alllit\"; action \"switch light\"; checks \"light is\", \"all lights\"; step \"Light up\"", 11, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic).gameObject, -1, 30);
		}
	}
}
