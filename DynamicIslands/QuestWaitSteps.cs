using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Quest step kinds of 3.5 (feat35a) that wait for the world, not for a deed: the behaviour checks of 11-04 (GapBehaviours,
	/// GapWorld) as quest steps.
	///   - "time" ("Be there at a time"): target = day, night, dawn, dusk, an hour range "20-6" or an hour "12" (commas = any of them).
	///   - "weather" ("Be there in weather"): target = clear, calm, fog, waves, rain, storm, snow (commas = any of them).
	///   - "counter" ("Counter reaches"): target = a counter's name (the "count" action's), count = the value it must reach.
	///   - "players" ("Players in a zone"): target = a trigger zone, count = players standing in it at once (never more than
	///     the players online: one player alone is enough, as for co-op zones).
	/// All four are counted by the host (QuestTracker.CheckCounted, only while a player is at the island) and move on for
	/// everyone through the normal quest step message; the panel shows each machine's own view (counters come from the host).
	/// </summary>
	public static class QuestWaitSteps
	{
		public const string Time = "time", Weather = "weather", Counter = "counter", Players = "players";

		public static readonly string[] Kinds = { Time, Weather, Counter, Players };

		public static bool Counted(string type) { return type == Time || type == Weather || type == Counter || type == Players; }

		/// <summary>The editor's count field: only counter and players steps have a number.</summary>
		public static bool HasCount(string type) { return type == Counter || type == Players; }

		/// <summary>Steps with no count of their own (the editor hides the field).</summary>
		public static bool NoCount(string type) { return type == Time || type == Weather; }

		/// <summary>How far the step is (QuestTracker.Found): time and weather 0 or done, a counter its value, players the count in the zone.</summary>
		public static int Found(IslandWorldState.Entry e, IslandQuest.Step s)
		{
			int need = Mathf.Max(1, s.Count);
			switch (s.Type)
			{
				case Time: return GapBehaviours.TimeNow(s.Target) ? need : 0;
				case Weather: return GapBehaviours.WeatherMatches(s.Target, GapBehaviours.WeatherNow) ? need : 0;
				case Counter: return s.Target.Trim().Length > 0 ? GapBehaviours.Counter(e, s.Target) : 0;
				case Players:
					TriggerZone z = GapWorld.FindZone(e, -1, s.Target);
					if (z == null || s.Target.Trim().Length == 0) return 0;
					List<Network_Player> players = GapWorld.LivingPlayers();
					int count = GapWorld.Count(z, players);
					// (as a co-op zone: never more than the players online)
					return count >= GapWorld.Need(Mathf.Min(need, GapWorld.MaxPlayers), GapWorld.Online(players)) ? need : Mathf.Min(count, need - 1);
			}
			return 0;
		}

		/// <summary>A step's words (Step.Describe), or null.</summary>
		internal static string Describe(string type, string target, int count)
		{
			target = (target ?? "").Trim();
			switch (type)
			{
				case Time: return "Be here " + TimeWords(target);
				case Weather: return target.Length == 0 ? "Wait for the weather" : "Be here in " + string.Join(" or ", Split(target).ToArray());
				case Counter: return target.Length == 0 ? "Get a counter to " + count : "Get " + target + " to " + count;
				case Players:
					string who = count > 1 ? count + " players" : "a player";
					return target.Length == 0 ? "Stand together in a zone" : "Stand in " + target + " with " + who + " at once";
			}
			return null;
		}

		static List<string> Split(string spec) { return (spec ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList(); }

		static string TimeWords(string spec)
		{
			var parts = new List<string>();
			foreach (string s in Split(spec))
			{
				float from, to;
				switch (s)
				{
					case "day": parts.Add("by day"); continue;
					case "night": parts.Add("at night"); continue;
					case "dawn": parts.Add("at dawn"); continue;
					case "dusk": parts.Add("at dusk"); continue;
				}
				if (GapBehaviours.HourRange(s, out from, out to)) parts.Add("between " + Clock(from) + " and " + Clock(to));
			}
			return parts.Count == 0 ? "at the right time" : string.Join(" or ", parts.ToArray());
		}

		static string Clock(float h)
		{
			int m = Mathf.RoundToInt(h * 60f) % (24 * 60);
			return (m / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (m % 60).ToString("00", CultureInfo.InvariantCulture);
		}

		/// <summary>The names the quest editor's ▾ offers, or null.</summary>
		internal static List<DropList.Option> NamesFor(string type, IEnumerable<EditorGameObject> all)
		{
			switch (type)
			{
				case Time:
					return new List<DropList.Option>
					{
						new DropList.Option("day", "day", "While the sun is up"), new DropList.Option("night", "night", "While the sun is down"),
						new DropList.Option("dawn", "dawn", "5:00 to 8:00"), new DropList.Option("dusk", "dusk", "18:00 to 21:00"),
						new DropList.Option("20-6", "20-6", "An hour range: 20:00 to 6:00 (past midnight); type your own, e.g. 12-14 or 12"),
					};
				case Weather:
					return GapBehaviours.WeatherNames.Where(w => w != "raft").Select(w => new DropList.Option(w, w, w == "storm" ? "Raft's storm (rain counts too)" : "Raft's " + w + " weather")).ToList();
				case Counter:
					return CounterNames(all.Select(g => (IDictionary<string, string>)g.Props))
						.Select(n => new DropList.Option(n, n, "A counter a count action on this island changes")).ToList();
				case Players:
					return all.Where(g => g.GameObjectName == ContentCatalog.TriggerZone).Select(g => ObjectProps.Get(g.Props, ObjectProps.ZoneId).Trim()).Where(n => n.Length > 0)
						.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
						.Select(n => new DropList.Option(n, n, "A trigger zone on this island")).ToList();
			}
			return null;
		}

		/// <summary>The counters the "count" actions of these objects (or the island's own events) change.</summary>
		public static List<string> CounterNames(IEnumerable<IDictionary<string, string>> props)
		{
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IDictionary<string, string> p in props)
			{
				if (p == null) continue;
				foreach (KeyValuePair<string, string> kv in p)
				{
					if (!kv.Key.StartsWith("on.") && !kv.Key.StartsWith("else.")) continue;
					foreach (string line in (kv.Value ?? "").Split('\n'))
					{
						string[] f = line.Split('|');
						if (f.Length >= 2 && f[0].Trim() == GapBehaviours.CountVerb && f[1].Trim().Length > 0) names.Add(f[1].Trim());
					}
				}
			}
			return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>What is wrong with a step of these kinds (null = fine; plan check, quest editor). zones = the island's zone ids
		/// (null: not known); counters = the counters its count actions change (null: not known).</summary>
		public static string StepProblem(ICollection<string> zones, ICollection<string> counters, string type, string target, int count)
		{
			target = (target ?? "").Trim();
			switch (type)
			{
				case Time:
					if (target.Length == 0) return "it names no time (day, night, dawn, dusk or hours like 20-6)";
					foreach (string s in Split(target))
					{
						float a, b;
						if (s != "day" && s != "night" && s != "dawn" && s != "dusk" && !GapBehaviours.HourRange(s, out a, out b))
							return "'" + s + "' is no time: write day, night, dawn, dusk or hours like 20-6";
					}
					break;
				case Weather:
					if (target.Length == 0) return "it names no weather (clear, calm, fog, waves, rain, storm, snow)";
					foreach (string s in Split(target))
						if (!GapBehaviours.WeatherNames.Contains(s) || s == "raft") return "'" + s + "' is no weather: write clear, calm, fog, waves, rain, storm or snow";
					break;
				case Counter:
					if (target.Length == 0) return "it names no counter";
					if (counters != null && !counters.Contains(target)) return "no count action on the island changes the counter '" + target + "'";
					break;
				case Players:
					if (target.Length == 0) return "it names no trigger zone to stand in";
					if (zones != null && !zones.Contains(target)) return "the island has no trigger zone '" + target + "'" + (zones.Count > 0 ? " (it has " + string.Join(", ", zones.ToArray()) + ")" : "");
					if (count > GapWorld.MaxPlayers) return "it wants " + count + " players; Raft has at most " + GapWorld.MaxPlayers;
					break;
			}
			return null;
		}

		/// <summary>QuestTest: what the world says now for the steps of these kinds ("" when the island has none of them).</summary>
		internal static string StateLine(IslandWorldState.Entry e, IslandQuest q, int step)
		{
			if (step >= q.Steps.Count || !Counted(q.Steps[step].Type)) return "";
			IslandQuest.Step s = q.Steps[step];
			float h = GapBehaviours.Hour;
			string now = "now: " + (h >= 0f ? Clock(h) + (GapBehaviours.IsDay ? " day" : " night") : "no clock") + ", weather " + (GapBehaviours.WeatherNow.Length > 0 ? GapBehaviours.WeatherNow : "?");
			if (s.Type == Counter) now += ", counter '" + s.Target + "' = " + GapBehaviours.Counter(e, s.Target);
			if (s.Type == Players) now += ", " + Mathf.Min(Found(e, s), s.Count) + " of " + s.Count + " players in '" + s.Target + "'";
			return now;
		}
	}
}
