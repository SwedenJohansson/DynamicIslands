using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>The inspector's rows for the quest mechanics (2026-10-24, MechFeatures): a zone's push, pulse and stack count,
	/// a lure, a piece players carry, a boss guarded while checks hold.</summary>
	public static partial class ObjectInspector
	{
		static readonly string[] pushDirs = { "off", "up", "down", "+x", "-x", "+z", "-z" };
		static readonly Vector3[] pushVecs = { Vector3.zero, Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

		/// <summary>A trigger zone's push (fan, wind, updraft, knockback), its on/off pulse and its stack count.</summary>
		static void MechZoneGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			MechFeatures.PushSpec s = MechFeatures.PushSpec.Parse(p);
			RectTransform g = UIKit.Group(root, "Push and pulse");
			RectTransform row = UIKit.Row(g, 26f, 3f, "PushDirs");
			for (int i = 0; i < pushDirs.Length; i++)
			{
				Vector3 dir = pushVecs[i];
				Button b = UIKit.Button(row, pushDirs[i], () =>
				{
					float force = s != null ? s.Force : 8f, kick = s != null ? s.Kick : 0f;
					Set(target, MechFeatures.PushProp, dir == Vector3.zero ? null : MechFeatures.PushSpec.Join(dir, force > 0f || kick > 0f ? force : 8f, kick));
					Refresh();
				}, i == 0 ? "No push" : "The zone pushes players standing in it this way (island axes: +z north, +x east)", -1, 26f, 11);
				UIKit.SetActive(b, i == 0 ? s == null : s != null && Vector3.Dot(s.Dir, dir) > 0.99f);
			}
			if (s != null)
			{
				Vector3 d = s.Dir;
				UIKit.Slider(g, "Push", 0f, MechFeatures.MaxForce, s.Force, v => v <= 0f ? "off" : v.ToString("0", CultureInfo.InvariantCulture) + " m/s",
					v => Set(target, MechFeatures.PushProp, MechFeatures.PushSpec.Join(d, Mathf.Round(v), s.Kick), null, false),
					"How fast it pushes while the player stands in it (a fan 6, a gale 12, an updraft up 10)", true);
				UIKit.Slider(g, "Knock", 0f, MechFeatures.MaxForce, s.Kick, v => v <= 0f ? "off" : v.ToString("0", CultureInfo.InvariantCulture),
					v => Set(target, MechFeatures.PushProp, MechFeatures.PushSpec.Join(d, s.Force, Mathf.Round(v)), null, false),
					"A one-off shove on stepping in (a knockback): set Push to off for a knock only", true);
			}
			UIKit.Label(g, "Pulse (on s, off s[, offset]) - empty = always on; the push, hazard and air only while on", 11, UIKit.TextMuted);
			InputField pulse = UIKit.Field(g, "3,2", ObjectProps.Get(p, MechFeatures.PulseProp), UIKit.RowHeight, "A steam vent, a firebomb: on for 3 s, off for 2 s, on the clock all players share");
			pulse.onEndEdit.AddListener(v => Set(target, MechFeatures.PulseProp, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(pulse);
			UIKit.Label(g, "Stack target (piece name|count) - with that many in it, its \"stack\" event fires", 11, UIKit.TextMuted);
			InputField stack = UIKit.Field(g, "Crate|4", ObjectProps.Get(p, MechFeatures.StackProp), UIKit.RowHeight,
				"Pieces players carry (on.use=carry|) with this name, standing in the zone; then on.stack=... runs once (host)");
			stack.onEndEdit.AddListener(v => { Set(target, MechFeatures.StackProp, v.Trim().Length > 0 ? v.Trim() : null); Refresh(); });
			amountFields.Add(stack);
		}

		/// <summary>A creature spot's guard by checks; an object's lure and carry.</summary>
		static void MechGroup(EditorGameObject target, ContentCatalog.CreatureKind kind)
		{
			Dictionary<string, string> p = target.Props;
			if (kind != null)
			{
				RectTransform b = UIKit.Group(root, "Guarded while");
				InputField sh = UIKit.TextArea(b, "state|Feeder|open", 40f,
					"Players' weapons glance off the animals while all these checks hold (one per line, like the behaviour window's checks): a boss guarded while its feeder runs");
				sh.text = ObjectProps.Get(p, MechFeatures.ShieldIf);
				sh.onEndEdit.AddListener(v => Set(target, MechFeatures.ShieldIf, v.Trim().Length > 0 ? v.Trim() : null));
				amountFields.Add(sh);
				return;
			}
			if (ContentCatalog.IsZone(target.GameObjectName)) return;
			RectTransform g = UIKit.Group(root, "Lure and carry");
			UIKit.Label(g, "Lure (creature spot name|radius) - while shown and open, its animals come and stay", 11, UIKit.TextMuted);
			InputField lure = UIKit.Field(g, "Goats|3", ObjectProps.Get(p, MechFeatures.LureProp), UIKit.RowHeight,
				"The animals of the creature spot with this name walk to this object and stay within the radius, calm, while it is open (a trough filled, a bait put out)");
			lure.onEndEdit.AddListener(v => Set(target, MechFeatures.LureProp, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(lure);
			UIKit.Label(g, "Bait (item|count) - used with it, it takes it and opens", 11, UIKit.TextMuted);
			InputField bait = UIKit.Field(g, "Raw_Beet|2", ObjectProps.Get(p, MechFeatures.LureItem), UIKit.RowHeight,
				"The item put in (food for a food lure, story:name for a story item); its use lines are made for you unless it has its own");
			bait.onEndEdit.AddListener(v => Set(target, MechFeatures.LureItem, v.Trim().Length > 0 ? v.Trim() : null));
			amountFields.Add(bait);
			string onUse = ObjectProps.Get(p, "on.use");
			bool carry = onUse.Trim() == "carry|";
			RectTransform row = UIKit.Row(g, 26f, 4f, "Carry");
			Button no = UIKit.Button(row, "Fixed", () => { if (carry) { Set(target, "on.use", null); Refresh(); } }, "Players can't pick it up", -1, 26f, 12);
			Button yes = UIKit.Button(row, "Players carry it", () =>
			{
				if (onUse.Trim().Length > 0 && !carry) { IslandInfo.ShowMessage("It has its own use lines - add carry| there (behaviour window)"); return; }
				Set(target, "on.use", "carry|"); Refresh();
			}, "The use key picks it up and puts it down in front (no jumping or ziplines while carrying; a hit drops it). Name it for a stack target", -1, 26f, 12);
			UIKit.SetActive(no, !carry);
			UIKit.SetActive(yes, carry);
			if (!carry) return;
			UIKit.Slider(g, "Snap", 0f, 2f, ObjectProps.GetFloat(p, MechFeatures.CarrySnap, 0f), v => v <= 0f ? "off" : v.ToString("0.0#", CultureInfo.InvariantCulture) + " m",
				v => Set(target, MechFeatures.CarrySnap, v < 0.05f ? null : ObjectProps.Format(Mathf.Round(v * 4f) / 4f), null, false),
				"Put down on this grid and turned in 90-degree steps (a neat stack, a bridge of planks)", true);
		}
	}
}
