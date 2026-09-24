using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommandUndoRedo;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The extra data a placed object can carry (saved per object in .island format 4). Plain string keys and values,
	/// namespaced by feature:
	///   creature.count / .health / .damage / .speed / .size / .respawn   creature spawn points (ContentCatalog creatures)
	///   note.title / note.text                                            readable notes: any object can carry one
	///   tint.color (#RRGGBB) / tint.amount (0..1)                         a colour tint, for any object and creatures
	/// Values that are missing mean the default, so only what the builder changed is stored.
	/// </summary>
	public static class ObjectProps
	{
		public const string CreatureCount = "creature.count", CreatureHealth = "creature.health", CreatureDamage = "creature.damage",
			CreatureSpeed = "creature.speed", CreatureSize = "creature.size", CreatureRespawn = "creature.respawn";
		public const string NoteTitle = "note.title", NoteText = "note.text";
		public const string TintColor = "tint.color", TintAmount = "tint.amount";

		public const int MaxCount = 8;
		public const float MinMultiplier = 0.25f, MaxMultiplier = 4f, MinSize = 0.5f, MaxSize = 2.5f;

		/// <summary>What an object starts with when it is placed from the list (a note gets a title to edit).</summary>
		public static Dictionary<string, string> Defaults(string name)
		{
			var props = new Dictionary<string, string>();
			if (ContentCatalog.IsNoteObject(name))
			{
				props[NoteTitle] = ContentCatalog.DefaultNoteTitle(name);
				props[NoteText] = "";
			}
			return props;
		}

		public static bool IsNote(string name, IDictionary<string, string> props)
		{
			return ContentCatalog.IsNoteObject(name) || (props != null && (props.ContainsKey(NoteTitle) || props.ContainsKey(NoteText)));
		}

		#region Typed access

		public static string Get(IDictionary<string, string> props, string key, string fallback = "")
		{
			string v;
			return props != null && props.TryGetValue(key, out v) && v != null ? v : fallback;
		}

		public static float GetFloat(IDictionary<string, string> props, string key, float fallback)
		{
			float f;
			return float.TryParse(Get(props, key, null), NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !float.IsNaN(f) && !float.IsInfinity(f) ? f : fallback;
		}

		public static int GetInt(IDictionary<string, string> props, string key, int fallback)
		{
			int i;
			return int.TryParse(Get(props, key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : fallback;
		}

		public static bool GetBool(IDictionary<string, string> props, string key, bool fallback)
		{
			string v = Get(props, key, null);
			return v == null ? fallback : v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
		}

		public static string Format(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }

		/// <summary>A copy with key set to value; the default value (or null/empty) removes the key, so files only hold changes.</summary>
		public static Dictionary<string, string> With(IDictionary<string, string> props, string key, string value, string defaultValue = null)
		{
			var copy = props != null ? new Dictionary<string, string>(props) : new Dictionary<string, string>();
			if (value == null || (defaultValue != null && value == defaultValue)) copy.Remove(key);
			else copy[key] = value;
			return copy;
		}

		#endregion

		#region Creature settings (with their limits)

		public static int Count(IDictionary<string, string> p) { return Mathf.Clamp(GetInt(p, CreatureCount, 1), 1, MaxCount); }
		public static float Health(IDictionary<string, string> p) { return Mathf.Clamp(GetFloat(p, CreatureHealth, 1f), MinMultiplier, MaxMultiplier); }
		public static float Damage(IDictionary<string, string> p) { return Mathf.Clamp(GetFloat(p, CreatureDamage, 1f), 0f, MaxMultiplier); }
		public static float Speed(IDictionary<string, string> p) { return Mathf.Clamp(GetFloat(p, CreatureSpeed, 1f), MinMultiplier, 2.5f); }
		public static float Size(IDictionary<string, string> p) { return Mathf.Clamp(GetFloat(p, CreatureSize, 1f), MinSize, MaxSize); }
		public static bool Respawns(IDictionary<string, string> p) { return GetBool(p, CreatureRespawn, true); }

		/// <summary>Stat presets for the creature editor: health, damage and speed multipliers.</summary>
		public static readonly KeyValuePair<string, float[]>[] Presets =
		{
			new KeyValuePair<string, float[]>("Easy", new[] { 0.5f, 0.5f, 0.8f }),
			new KeyValuePair<string, float[]>("Normal", new[] { 1f, 1f, 1f }),
			new KeyValuePair<string, float[]>("Hard", new[] { 2f, 1.5f, 1.2f }),
			new KeyValuePair<string, float[]>("Boss", new[] { 4f, 2.5f, 1.3f }),
		};

		#endregion

		#region Tint

		public static bool HasTint(IDictionary<string, string> p) { return p != null && p.ContainsKey(TintColor) && TintStrength(p) > 0.001f; }

		public static Color Tint(IDictionary<string, string> p)
		{
			Color c;
			return ColorUtility.TryParseHtmlString(Get(p, TintColor, "#FFFFFF"), out c) ? c : Color.white;
		}

		public static float TintStrength(IDictionary<string, string> p) { return Mathf.Clamp01(GetFloat(p, TintAmount, 1f)); }

		public static string ColorText(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

		/// <summary>Colour properties Raft's and Unity's shaders use for the main colour, in order of preference.</summary>
		static readonly string[] ColorProperties = { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_AlbedoColor" };

		/// <summary>
		/// Tints every renderer under root by multiplying its material colour (amount 0 = untouched). Uses property
		/// blocks, so the shared materials of other objects are never changed; Raft's own damage flash reads the block
		/// back first, so both work together. Returns how many materials could be tinted.
		/// </summary>
		public static int ApplyTint(GameObject root, Color tint, float amount)
		{
			int tinted = 0;
			if (root == null) return 0;
			Color factor = Color.Lerp(Color.white, tint, Mathf.Clamp01(amount));
			foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
			{
				if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r.GetComponent<TextMesh>() != null || r.name == ContentCatalog.MarkerOnly) continue;
				Material[] mats = r.sharedMaterials;
				for (int i = 0; i < mats.Length; i++)
				{
					Material m = mats[i];
					if (m == null) continue;
					string prop = ColorProperties.FirstOrDefault(m.HasProperty);
					if (prop == null) continue;
					var block = new MaterialPropertyBlock();
					if (mats.Length == 1) r.GetPropertyBlock(block); else r.GetPropertyBlock(block, i);
					Color baseColor = m.GetColor(prop);
					block.SetColor(prop, new Color(baseColor.r * factor.r, baseColor.g * factor.g, baseColor.b * factor.b, baseColor.a));
					if (mats.Length == 1) r.SetPropertyBlock(block); else r.SetPropertyBlock(block, i);
					tinted++;
				}
			}
			return tinted;
		}

		public static void ApplyTint(GameObject root, IDictionary<string, string> props)
		{
			if (HasTint(props)) ApplyTint(root, Tint(props), TintStrength(props));
			else ApplyTint(root, Color.white, 0f);
		}

		#endregion

		/// <summary>Editor: shows an object's settings on it (tint, a creature marker's size and herd, a note's title).</summary>
		public static void ApplyInEditor(GameObject go, IDictionary<string, string> props)
		{
			if (go == null) return;
			ApplyTint(go, props);
			ContentCatalog.ShowInEditor(go, props);
		}
	}

	/// <summary>Undo step for changing a placed object's settings (creature stats, note text, tint).</summary>
	public class PropsCommand : ICommand
	{
		readonly EditorGameObject target;
		readonly Dictionary<string, string> before, after;

		public PropsCommand(EditorGameObject target, Dictionary<string, string> before, Dictionary<string, string> after)
		{
			this.target = target;
			this.before = new Dictionary<string, string>(before);
			this.after = new Dictionary<string, string>(after);
		}

		public void Execute() { Set(after); }
		public void UnExecute() { Set(before); }

		void Set(Dictionary<string, string> props)
		{
			if (target == null) return;
			target.Props = new Dictionary<string, string>(props);
			ObjectProps.ApplyInEditor(target.gameObject, target.Props);
			ObjectInspector.Refresh();
		}

		/// <summary>
		/// Changes an object's settings as an undoable step. Slider drags arrive as many small changes: those within a
		/// moment of each other on the same object and setting merge into one undo step.
		/// </summary>
		public static void Change(EditorGameObject target, Dictionary<string, string> after, string mergeKey = null)
		{
			if (target == null) return;
			Dictionary<string, string> before = target.Props ?? new Dictionary<string, string>();
			if (Same(before, after)) return;
			float now = Time.unscaledTime;
			// (only while that step is still what the object shows: not after an undo)
			if (mergeKey != null && lastTarget == target && lastKey == mergeKey && now - lastTime < 1.5f && lastCommand != null && Same(before, lastCommand.after))
			{
				lastCommand.after.Clear();
				foreach (var kv in after) lastCommand.after[kv.Key] = kv.Value;
				lastCommand.Set(after);
			}
			else
			{
				lastCommand = new PropsCommand(target, before, after);
				UndoRedoManager.Execute(lastCommand);
			}
			lastTarget = target; lastKey = mergeKey; lastTime = now;
		}

		static PropsCommand lastCommand;
		static EditorGameObject lastTarget;
		static string lastKey;
		static float lastTime;

		static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
		{
			return a.Count == b.Count && a.All(kv => { string v; return b.TryGetValue(kv.Key, out v) && v == kv.Value; });
		}
	}
}
