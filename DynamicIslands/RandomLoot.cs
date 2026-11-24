using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A chest that gives a random part of its list (ROADMAP QE14). loot.items stays the list (so older versions still
	/// give the whole list); loot.mode=random turns the roll on, loot.chance holds each item's chance in percent
	/// ("Plank=25;Nail=100", missing = 100) and loot.pick = how many different items to pick, weighted by their chance
	/// (0 or missing = every item rolls its own chance). The roll is seeded by the island, the chest and the day the
	/// fill is opened: the same on every machine, new for every fill, with no message of its own.
	/// </summary>
	public static class RandomLoot
	{
		public const string Mode = "loot.mode";
		public const string Chance = "loot.chance";
		public const string Pick = "loot.pick";
		public const string RandomMode = "random";

		public static bool IsRandom(IDictionary<string, string> p) { return ObjectProps.Get(p, Mode).Trim().ToLowerInvariant() == RandomMode; }

		public static int PickCount(IDictionary<string, string> p)
		{
			int n;
			return int.TryParse(ObjectProps.Get(p, Pick).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? Mathf.Clamp(n, 0, 99) : 0;
		}

		/// <summary>Item name -> chance in percent (1-100); items not named have 100.</summary>
		public static Dictionary<string, int> Chances(IDictionary<string, string> p)
		{
			var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (string part in ObjectProps.Get(p, Chance).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int eq = part.LastIndexOf('=');
				int pct;
				if (eq <= 0 || !int.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pct)) continue;
				d[part.Substring(0, eq).Trim()] = Mathf.Clamp(pct, 0, 100);
			}
			return d;
		}

		public static int ChanceOf(Dictionary<string, int> chances, string item)
		{
			int c;
			return chances.TryGetValue(item, out c) ? c : 100;
		}

		public static string ChanceText(Dictionary<string, int> chances)
		{
			return string.Join(";", chances.Where(c => c.Value < 100).OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
				.Select(c => c.Key + "=" + c.Value.ToString(CultureInfo.InvariantCulture)).ToArray());
		}

		/// <summary>The props with one item's chance set (100 = the key entry dropped).</summary>
		public static Dictionary<string, string> WithChance(IDictionary<string, string> p, string item, int pct)
		{
			Dictionary<string, int> c = Chances(p);
			c[item] = Mathf.Clamp(pct, 0, 100);
			string text = ChanceText(c);
			return ObjectProps.With(p, Chance, text.Length > 0 ? text : null);
		}

		/// <summary>A stable hash (string.GetHashCode differs between runtimes).</summary>
		static int Hash(string s)
		{
			unchecked
			{
				int h = (int)2166136261;
				foreach (char ch in s ?? "") { h ^= ch; h *= 16777619; }
				return h;
			}
		}

		public static int Seed(string island, int ordinal, int day)
		{
			unchecked { return Hash((island ?? "").ToLowerInvariant()) * 31 + ordinal * 7919 + day * 104729; }
		}

		/// <summary>What one fill gives: the whole list when the chest isn't random.</summary>
		public static List<KeyValuePair<string, int>> Roll(IDictionary<string, string> p, List<KeyValuePair<string, int>> items, int seed)
		{
			if (!IsRandom(p) || items.Count == 0) return items.ToList();
			Dictionary<string, int> chances = Chances(p);
			var rng = new System.Random(seed);
			int pick = PickCount(p);
			var result = new List<KeyValuePair<string, int>>();
			if (pick <= 0)
			{
				foreach (var it in items)
					if (rng.Next(100) < ChanceOf(chances, it.Key)) result.Add(it);
				return result;
			}
			// N different entries, each draw weighted by the chance of what is left
			var left = items.Select((it, i) => i).Where(i => ChanceOf(chances, items[i].Key) > 0).ToList();
			var chosen = new List<int>();
			for (int k = 0; k < pick && left.Count > 0; k++)
			{
				int total = left.Sum(i => ChanceOf(chances, items[i].Key));
				int r = rng.Next(total);
				foreach (int i in left)
				{
					r -= ChanceOf(chances, items[i].Key);
					if (r < 0) { chosen.Add(i); left.Remove(i); break; }
				}
			}
			chosen.Sort(); // (in list order)
			foreach (int i in chosen) result.Add(items[i]);
			return result;
		}

		/// <summary>Each entry's odds of being in one fill, in percent (exact for "each rolls", by many test rolls for "pick N").</summary>
		public static List<float> Odds(IDictionary<string, string> p, List<KeyValuePair<string, int>> items, out float nothing)
		{
			nothing = 0f;
			var odds = new List<float>();
			if (!IsRandom(p)) { foreach (var it in items) odds.Add(100f); return odds; }
			Dictionary<string, int> chances = Chances(p);
			int pick = PickCount(p);
			if (pick <= 0)
			{
				float none = 1f;
				foreach (var it in items) { float c = ChanceOf(chances, it.Key); odds.Add(c); none *= 1f - c / 100f; }
				nothing = none * 100f;
				return odds;
			}
			const int Runs = 2000;
			var hits = new int[items.Count];
			int empty = 0;
			for (int s = 0; s < Runs; s++)
			{
				List<KeyValuePair<string, int>> got = Roll(p, items, 977 + s * 7919);
				if (got.Count == 0) empty++;
				// (entries are matched by position: Roll keeps list order and takes each entry once)
				int j = 0;
				for (int i = 0; i < items.Count && j < got.Count; i++)
					if (got[j].Key == items[i].Key && got[j].Value == items[i].Value) { hits[i]++; j++; }
			}
			foreach (int h in hits) odds.Add(Mathf.Round(h * 1000f / Runs) / 10f);
			nothing = empty * 100f / Runs;
			return odds;
		}

		/// <summary>One line for the inspector: "Gives 2 of 5, weighted by chance" / "Each item rolls its chance; empty 12%".</summary>
		public static string Summary(IDictionary<string, string> p, List<KeyValuePair<string, int>> items)
		{
			if (!IsRandom(p)) return "";
			float nothing;
			Odds(p, items, out nothing);
			int pick = PickCount(p);
			string head = pick > 0 ? "Picks " + Mathf.Min(pick, items.Count) + " of " + items.Count + ", weighted by chance" : "Each item rolls its own chance";
			return head + (nothing >= 0.05f ? "; empty " + nothing.ToString("0.#", CultureInfo.InvariantCulture) + "% of fills" : "") + ". New roll every fill, the same for every player.";
		}

		/// <summary>Recipe form: "Plank*4@25;Nail*2" -> loot.items without the @, and the chances (null when none had one).</summary>
		public static string SplitRecipe(string text, out string chances)
		{
			var items = new List<string>();
			var c = new List<string>();
			foreach (string raw in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string part = raw.Trim();
				int at = part.LastIndexOf('@');
				if (at > 0)
				{
					string pct = part.Substring(at + 1).Trim().TrimEnd('%');
					part = part.Substring(0, at).Trim();
					int star = part.LastIndexOf('*');
					string name = (star > 0 ? part.Substring(0, star) : part).Trim();
					int n;
					if (int.TryParse(pct, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n < 100) c.Add(name + "=" + Mathf.Clamp(n, 0, 100).ToString(CultureInfo.InvariantCulture));
				}
				items.Add(part);
			}
			chances = c.Count > 0 || (text ?? "").Contains("@") ? string.Join(";", c.ToArray()) : null;
			return string.Join(";", items.ToArray());
		}
	}
}
