using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AzureSky;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// More behaviour actions and checks (gap ideas 2, 5, 6, 7, 10 - 2026-11-04). PlayerFeatures and Behaviours call in here.
	///   time|night            check: the time of day - day, night, dawn, dusk or an hour range "20-6" (Raft's clock, the
	///                         same on every machine: Raft keeps it in step). A list "dawn,dusk" = any of them.
	///   weather|rain|10       shared action (host): Raft's weather becomes rain (clear, calm, fog, waves, rain, storm,
	///                         snow; raft = Raft chooses again; Raft's seas have no fog weather) and is held for 10 minutes (empty = 10), then Raft chooses
	///                         again. Raft sends its weather to every player.
	///   weather|rain,storm    check: the weather now is one of these.
	///   random||3             the next 3 actions (empty = 2; stops at a wait): only one of them runs, picked at random. The
	///                         pick is the same on every machine: the event's seed goes with its network message.
	///   count|Fish|+1         shared action (host): the island's counter Fish goes up by 1 (+N, -N, =N; empty = +1;
	///                         never below 0). Saved with the world's island state (CounterBase), sent to every player.
	///   count|Fish|>=3        check: the counter is at least 3 (>=, >, <=, <, =, !=; a bare number = at least).
	///   heal||drink 40        personal action: the player who set it off gets health / food / drink back (empty or "all" =
	///                         all three full; "drink" = thirst full; "health 30" = 30 points; a list "food,drink").
	///   Story_Spring          a drinking spring (Zones &amp; triggers): its use key quenches the player's thirst (heal||drink).
	/// </summary>
	public static class GapBehaviours
	{
		public const string TimeCheck = "time", WeatherVerb = "weather", WeatherCheck = "weather", RandomVerb = "random",
			CountVerb = "count", CountCheck = "count", HealVerb = "heal";
		public const string SpringName = "Story_Spring", SpringUse = "Drink from the spring";
		public const string SpringHint = "a drinking spring: its use key quenches the player's thirst (the heal action - change it to heal food, health or all)";

		/// <summary>The island's counters in its state: CounterBase + the name's hash (20 bits); Yield = the value.</summary>
		public const int CounterBase = 0x7400000;
		public const int MaxCounter = 999999, MaxRandom = 20, DefaultWeatherMinutes = 10;

		public static bool HasTarget(string verb) { return false; }
		public static bool HasArg(string verb) { return false; }
		public static bool Shared(string verb) { return verb == WeatherVerb || verb == CountVerb; }
		public static bool Owns(string verb) { return verb == WeatherVerb || verb == RandomVerb || verb == CountVerb || verb == HealVerb; }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] " + msg); }

		#region Time of day (2)

		/// <summary>Tests: the hour whatever the sky says (null = the sky).</summary>
		internal static float? TestHour;
		static AzureSkyController sky;

		/// <summary>The hour of Raft's day (0..24), or -1 when there is no sky.</summary>
		public static float Hour
		{
			get
			{
				if (TestHour.HasValue) return TestHour.Value;
				if (sky == null) sky = RaftSkySea.Sky ?? UnityEngine.Object.FindObjectOfType<AzureSkyController>();
				return sky != null && sky.timeOfDay != null ? sky.timeOfDay.hour : -1f;
			}
		}

		/// <summary>Raft's daytime (the sun up), or for a test hour 6:00 to 19:00.</summary>
		public static bool IsDay
		{
			get
			{
				if (TestHour.HasValue) return TestHour.Value >= 6f && TestHour.Value < 19f;
				if (sky == null) sky = RaftSkySea.Sky ?? UnityEngine.Object.FindObjectOfType<AzureSkyController>();
				return sky == null || sky.IsDaytime;
			}
		}

		/// <summary>Whether a time check's words ("night", "dawn,dusk", "20-6", "12") hold at an hour.</summary>
		public static bool TimeMatches(string spec, float hour, bool day)
		{
			foreach (string raw in (spec ?? "").Split(','))
			{
				string s = raw.Trim().ToLowerInvariant();
				if (s.Length == 0) continue;
				switch (s)
				{
					case "day": if (day) return true; continue;
					case "night": if (!day) return true; continue;
					case "dawn": if (InHours(hour, 5f, 8f)) return true; continue;
					case "dusk": if (InHours(hour, 18f, 21f)) return true; continue;
				}
				float from, to;
				if (HourRange(s, out from, out to) && InHours(hour, from, to)) return true;
			}
			return false;
		}

		/// <summary>"20-6" (past midnight), "6-12", or one hour "12" (12:00 to 13:00).</summary>
		public static bool HourRange(string s, out float from, out float to)
		{
			from = to = 0f;
			string[] p = (s ?? "").Split('-');
			if (p.Length == 1 && TryHour(p[0], out from)) { to = from + 1f; return true; }
			return p.Length == 2 && TryHour(p[0], out from) && TryHour(p[1], out to);
		}

		static bool TryHour(string s, out float h)
		{
			s = (s ?? "").Trim().Replace(':', '.');
			if (s.EndsWith(".30")) s = s.Substring(0, s.Length - 3) + ".5";
			return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out h) && h >= 0f && h <= 24f;
		}

		static bool InHours(float hour, float from, float to)
		{
			if (hour < 0f) return false;
			return from <= to ? hour >= from && hour < to : hour >= from || hour < to;
		}

		/// <summary>Whether the time check holds now, on this machine (Raft keeps every player's clock in step).</summary>
		public static bool TimeNow(string spec) { return TimeMatches(spec, Hour, IsDay); }

		static string DescribeTime(string spec)
		{
			var parts = new List<string>();
			foreach (string raw in (spec ?? "").Split(','))
			{
				string s = raw.Trim().ToLowerInvariant();
				float from, to;
				if (s == "day" || s == "night") parts.Add("it is " + s);
				else if (s == "dawn") parts.Add("it is dawn (5:00-8:00)");
				else if (s == "dusk") parts.Add("it is dusk (18:00-21:00)");
				else if (HourRange(s, out from, out to)) parts.Add("the time is " + Clock(from) + "-" + Clock(to));
				else if (s.Length > 0) parts.Add("'" + s + "' (not a time: day, night, dawn, dusk or 20-6)");
			}
			return parts.Count == 0 ? "(no time)" : string.Join(" or ", parts.ToArray());
		}

		static string Clock(float h) { int m = Mathf.RoundToInt(h * 60f); return (m / 60) + ":" + (m % 60).ToString("00"); }

		#endregion

		#region Weather (5)

		/// <summary>Tests: the weather a check sees (null = Raft's).</summary>
		internal static string TestWeather;
		static WeatherManager manager;
		static WeatherManager Manager { get { if (manager == null) manager = UnityEngine.Object.FindObjectOfType<WeatherManager>(); return manager; } }

		public static readonly string[] WeatherNames = { "clear", "calm", "fog", "waves", "rain", "storm", "snow", "raft" };

		/// <summary>The weather name of one of Raft's weathers.</summary>
		public static string NameOf(UniqueWeatherType t)
		{
			switch (t)
			{
				case UniqueWeatherType.Calm: return "calm";
				case UniqueWeatherType.Fog: return "fog";
				case UniqueWeatherType.BigWaves: return "waves";
				case UniqueWeatherType.Rain: return "rain";
				case UniqueWeatherType.Snow: return "snow";
				case UniqueWeatherType.SnowRough: return "storm";
			}
			return "clear";
		}

		/// <summary>Raft's weather now ("clear", "rain"...; "" without a world).</summary>
		public static string WeatherNow
		{
			get
			{
				if (TestWeather != null) return TestWeather;
				WeatherManager wm = Manager;
				try
				{
					if (wm == null) return "";
					// Raft fades the old weather out first (some seconds): a weather action's weather counts from the start
					if (changingTo.HasValue && Changing(wm)) return NameOf(changingTo.Value);
					changingTo = null;
					return NameOf(wm.GetCurrentWeatherType());
				}
				catch { return ""; }
			}
		}

		/// <summary>The weather a weather action started and Raft is still fading in.</summary>
		static UniqueWeatherType? changingTo;
		static bool Changing(WeatherManager wm) { return Traverse.Create(wm).Field("changingWeather").GetValue<bool>(); }

		/// <summary>Whether a weather check's names ("rain,storm") hold for a weather. storm = Raft's rain storm or rough snow.</summary>
		public static bool WeatherMatches(string spec, string now)
		{
			foreach (string raw in (spec ?? "").Split(','))
			{
				string s = raw.Trim().ToLowerInvariant();
				if (s.Length == 0) continue;
				if (s == now) return true;
				if (s == "storm" && now == "rain") return true;
			}
			return false;
		}

		/// <summary>Host: the shared action "weather|name|minutes".</summary>
		internal static void HostWeather(IslandWorldState.Entry e, ObjAction a)
		{
			if (!Raft_Network.IsHost) return;
			string want = (a.Target ?? "").Trim().ToLowerInvariant();
			WeatherManager wm = Manager;
			if (wm == null) { Log("Weather '" + want + "': no weather in this world"); return; }
			if (want == "raft" || want == "auto")
			{
				wm.WeatherTimer = 5f;
				changingTo = null;
				Log("Weather on '" + (e != null ? e.HostName : "?") + "': Raft chooses its weather again");
				return;
			}
			UniqueWeatherType type;
			if (!WeatherType(wm, want, out type)) { Log("Weather '" + want + "' isn't one of " + string.Join(", ", WeatherNames)); return; }
			try
			{
				bool has = true;
				if (wm.GetCurrentWeatherType() != type)
				{
					// (Raft looks for the weather in all its seas' lists; one it has starts fading in at once)
					wm.SetWeather(type, false);
					has = Changing(wm) || wm.GetCurrentWeatherType() == type;
					if (!has)
					{
						// (a weather in the scene but in none of the seas' lists)
						Weather w = StormSeas.FindWeather(type);
						if (w != null) { wm.SetWeather(w, false); has = Changing(wm) || wm.GetCurrentWeatherType() == type; }
					}
					changingTo = has ? type : (UniqueWeatherType?)null;
				}
				wm.WeatherTimer = Minutes(a.Arg) * 60f;
				Log("Weather on '" + (e != null ? e.HostName : "?") + "': " + want + " (" + type + ") for " + Minutes(a.Arg) + " min" + (has ? "" : " - Raft has no such weather (it has " + Available() + "), it stays " + wm.GetCurrentWeatherType()));
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Weather '" + want + "' failed: " + ex.Message); }
		}

		/// <summary>The weathers Raft has in this world ("clear, calm, rain, ..."), for the log.</summary>
		internal static string Available()
		{
			var names = new List<string>();
			foreach (Weather w in Resources.FindObjectsOfTypeAll<Weather>())
			{
				if (w == null || !w.gameObject.scene.IsValid()) continue;
				SO_Weather so = Traverse.Create(w).Field("so_weather").GetValue<SO_Weather>();
				if (so != null && !names.Contains(NameOf(so.uniqueWeatherType) + "/" + so.uniqueWeatherType)) names.Add(NameOf(so.uniqueWeatherType) + "/" + so.uniqueWeatherType);
			}
			return names.Count > 0 ? string.Join(", ", names.ToArray()) : "none";
		}

		static bool WeatherType(WeatherManager wm, string name, out UniqueWeatherType t)
		{
			t = UniqueWeatherType.Default;
			switch (name)
			{
				case "clear": t = UniqueWeatherType.Default; return true;
				case "calm": t = UniqueWeatherType.Calm; return true;
				case "fog": t = UniqueWeatherType.Fog; return true;
				case "waves": t = UniqueWeatherType.BigWaves; return true;
				case "rain": t = UniqueWeatherType.Rain; return true;
				case "snow": t = UniqueWeatherType.Snow; return true;
				case "storm": t = StormDays.StormWeather(wm); return true;
			}
			return false;
		}

		public static int Minutes(string arg)
		{
			int m;
			return int.TryParse((arg ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out m) && m > 0 ? Math.Min(m, 600) : DefaultWeatherMinutes;
		}

		#endregion

		#region Random choice (6)

		/// <summary>The seed of the event being run: the same on every machine for one firing (it goes with the event's
		/// message: IslandNetwork.SendEvent's Ids[1]), so a random choice picks the same action for the personal and the
		/// shared part.</summary>
		public static int Seed = 1;

		public static int NewSeed() { Seed = UnityEngine.Random.Range(1, int.MaxValue); return Seed; }

		/// <summary>How many actions a "random" line chooses from (2..20; empty = 2).</summary>
		public static int Options(ObjAction a)
		{
			int n;
			string s = (a.Arg ?? "").Trim().Length > 0 ? a.Arg : a.Target;
			return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? Mathf.Clamp(n, 2, MaxRandom) : 2;
		}

		/// <summary>The actions with each "random" line's group cut to the one it picks (the group: the next N actions, up to a
		/// wait). Waits stay, so a list's parts keep their numbers.</summary>
		public static List<ObjAction> Pick(List<ObjAction> actions, int seed)
		{
			if (actions == null || !actions.Any(a => a.Verb == RandomVerb)) return actions;
			var list = new List<ObjAction>();
			for (int i = 0; i < actions.Count; i++)
			{
				ObjAction a = actions[i];
				if (a.Verb != RandomVerb) { list.Add(a); continue; }
				var group = new List<ObjAction>();
				int n = Options(a), j = i + 1;
				for (; j < actions.Count && group.Count < n && actions[j].Verb != "wait"; j++) group.Add(actions[j]);
				if (group.Count > 0) list.Add(group[Choose(seed, i, group.Count)]);
				i = j - 1;
			}
			return list;
		}

		/// <summary>The pick of the random line at a place in the list: the same for the same seed on every machine.</summary>
		public static int Choose(int seed, int place, int count)
		{
			unchecked
			{
				uint h = (uint)seed ^ ((uint)(place + 1) * 2654435761u);
				h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
				return (int)(h % (uint)Math.Max(1, count));
			}
		}

		#endregion

		#region Counters (7)

		public static int CounterKey(string name)
		{
			int h = 17;
			foreach (char c in (name ?? "").Trim().ToLowerInvariant()) h = h * 31 + c;
			return CounterBase + (h & 0xFFFFF);
		}

		public static int Counter(IslandWorldState.Entry e, string name)
		{
			ObjectState s;
			return e != null && e.State.TryGetValue(CounterKey(name), out s) ? Math.Max(0, s.Yield) : 0;
		}

		static void SetCounter(IslandWorldState.Entry e, int key, int value)
		{
			e.State[key] = new ObjectState { Active = true, Yield = Mathf.Clamp(value, 0, MaxCounter), Day = Behaviours.Today };
		}

		/// <summary>A count action's new value: +N, -N, =N, N (= +N); empty = +1.</summary>
		public static int Counted(int now, string arg)
		{
			string s = (arg ?? "").Trim().Replace(" ", "");
			if (s.Length == 0) return now + 1;
			int n;
			if (s.StartsWith("=")) return int.TryParse(s.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : now;
			if (s.Equals("reset", StringComparison.OrdinalIgnoreCase)) return 0;
			return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n) ? now + n : now;
		}

		/// <summary>Host: the shared action "count|name|+1": the counter changes and every player is told.</summary>
		internal static void HostCount(IslandWorldState.Entry e, ObjAction a)
		{
			if (e == null || !Raft_Network.IsHost) return;
			string name = (a.Target ?? "").Trim();
			if (name.Length == 0) return;
			int key = CounterKey(name), was = Counter(e, name);
			SetCounter(e, key, Counted(was, a.Arg));
			int now = Counter(e, name);
			Log("Counter '" + name + "' on '" + e.HostName + "': " + was + " -> " + now);
			try { IslandNetwork.SendToEveryone(new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "count", Ids = new[] { e.Id }, Index = key, Count = now }); }
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Counter message: " + ex.Message); }
		}

		/// <summary>Player: the host's counter (MechFeatures.OnMessage, Name "count").</summary>
		internal static void OnCountMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost || msg.Ids == null || msg.Ids.Length == 0 || msg.Index < CounterBase || msg.Index > CounterBase + 0xFFFFF) return;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Id == msg.Ids[0]);
			if (e != null) SetCounter(e, msg.Index, msg.Count);
		}

		/// <summary>Whether a count check's words (">=3", "=0", "3" = at least 3; empty = at least 1) hold for a value.</summary>
		public static bool CountMatches(int value, string arg)
		{
			string s = (arg ?? "").Trim().Replace(" ", "");
			string op = ">=";
			foreach (string o in new[] { ">=", "<=", "!=", ">", "<", "=" })
				if (s.StartsWith(o)) { op = o; s = s.Substring(o.Length); break; }
			int n;
			if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) n = 1;
			switch (op)
			{
				case "<=": return value <= n;
				case "!=": return value != n;
				case ">": return value > n;
				case "<": return value < n;
				case "=": return value == n;
			}
			return value >= n;
		}

		static string DescribeCount(string arg)
		{
			string s = (arg ?? "").Trim().Replace(" ", "");
			if (s.Length == 0) return "goes up by 1";
			if (s.Equals("reset", StringComparison.OrdinalIgnoreCase)) return "goes back to 0";
			if (s.StartsWith("=")) return "is set to " + s.Substring(1);
			if (s.StartsWith("-")) return "goes down by " + s.Substring(1);
			return "goes up by " + s.TrimStart('+');
		}

		static string DescribeCountCheck(string arg)
		{
			string s = (arg ?? "").Trim().Replace(" ", "");
			if (s.Length == 0) return "is at least 1";
			if (s.StartsWith(">=")) return "is at least " + s.Substring(2);
			if (s.StartsWith("<=")) return "is at most " + s.Substring(2);
			if (s.StartsWith("!=")) return "is not " + s.Substring(2);
			if (s.StartsWith(">")) return "is over " + s.Substring(1);
			if (s.StartsWith("<")) return "is under " + s.Substring(1);
			if (s.StartsWith("=")) return "is " + s.Substring(1);
			return "is at least " + s;
		}

		#endregion

		#region Heal, feed, drink (10)

		/// <summary>The parts a heal action fills: health, food, drink, each with an amount (-1 = full).</summary>
		public static Dictionary<string, float> HealParts(string arg)
		{
			var d = new Dictionary<string, float>();
			string s = (arg ?? "").Trim().ToLowerInvariant();
			if (s.Length == 0 || s == "all") { d["health"] = -1f; d["food"] = -1f; d["drink"] = -1f; return d; }
			foreach (string raw in s.Split(','))
			{
				string[] w = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (w.Length == 0) continue;
				string what = w[0] == "hunger" || w[0] == "eat" ? "food" : w[0] == "thirst" || w[0] == "water" ? "drink" : w[0] == "hp" ? "health" : w[0];
				float n;
				float amount = w.Length > 1 && float.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out n) && n > 0f ? n : -1f;
				if (what == "all") { d["health"] = amount; d["food"] = amount; d["drink"] = amount; }
				else if (what == "health" || what == "food" || what == "drink") d[what] = amount;
			}
			return d;
		}

		/// <summary>The local player's health, food and drink filled (the personal action "heal").</summary>
		internal static void Heal(string arg)
		{
			Network_Player p = null;
			try { p = RAPI.GetLocalPlayer(); } catch { }
			if (p == null || p.Stats == null) return;
			PlayerStats s = p.Stats;
			if (s.IsDead) return;
			foreach (var kv in HealParts(arg))
			{
				try
				{
					switch (kv.Key)
					{
						case "health":
							if (s.stat_health != null) s.stat_health.Value = kv.Value < 0f ? s.stat_health.Max : Mathf.Min(s.stat_health.Max, s.stat_health.Value + kv.Value);
							break;
						case "food":
							if (s.stat_hunger != null) s.stat_hunger.Normal.Value = kv.Value < 0f ? s.stat_hunger.Normal.Max : Mathf.Min(s.stat_hunger.Normal.Max, s.stat_hunger.Normal.Value + kv.Value);
							break;
						case "drink":
							if (s.stat_thirst != null) s.stat_thirst.Normal.Value = kv.Value < 0f ? s.stat_thirst.Normal.Max : Mathf.Min(s.stat_thirst.Normal.Max, s.stat_thirst.Normal.Value + kv.Value);
							break;
					}
				}
				catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Heal " + kv.Key + ": " + ex.Message); }
			}
		}

		static string DescribeHeal(string arg)
		{
			Dictionary<string, float> d = HealParts(arg);
			if (d.Count == 0) return "heal: '" + arg + "' (not health, food, drink or all)";
			if (d.Count == 3 && d.Values.All(v => v < 0f)) return "fill the player's health, food and drink";
			return "give the player back " + string.Join(", ", d.Select(kv => (kv.Value < 0f ? "all their " : Mathf.RoundToInt(kv.Value) + " ") + kv.Key).ToArray());
		}

		public static bool IsSpring(string name) { return name == SpringName; }

		public static void Defaults(string name, Dictionary<string, string> props)
		{
			if (!IsSpring(name)) return;
			props[BehaviourProps.Use] = SpringUse;
			props["on.use"] = HealVerb + "||drink";
		}

		/// <summary>A spring's use lines as if the builder had written them: it quenches thirst unless its lines heal already.</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			if (props == null || !IsSpring(objectName)) return props;
			string use = ObjectProps.Get(props, "on.use");
			bool heals = ObjAction.ParseLines(use).Any(a => a.Verb == HealVerb);
			bool label = ObjectProps.Get(props, BehaviourProps.Use).Length > 0;
			if (heals && label) return props;
			var d = new Dictionary<string, string>(props);
			if (!heals) d["on.use"] = HealVerb + "||drink" + (use.Length > 0 ? "\n" + use : "");
			if (!label) d[BehaviourProps.Use] = SpringUse;
			return d;
		}

		/// <summary>World: a spring's editor name tag goes.</summary>
		internal static void Attach(GameObject go)
		{
			foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
				if (t.name == ContentCatalog.MarkerOnly && t.GetComponent<TextMesh>() != null) t.gameObject.SetActive(false);
		}

		public static GameObject BuildSpring()
		{
			var root = new GameObject(SpringName);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Transform m = new GameObject("Spring").transform;
			m.SetParent(root.transform, false);
			Material stone = ContentCatalog.MarkerMaterial(new Color(0.46f, 0.44f, 0.4f)), dark = ContentCatalog.MarkerMaterial(new Color(0.36f, 0.34f, 0.31f));
			Material water = ContentCatalog.MarkerMaterial(new Color(0.22f, 0.52f, 0.68f));
			// A ring of stones round a pool, the water a little below their tops; the stones and a stone footing reach
			// 0.8 m into the ground, so on a slope its low side still stands on the ground
			for (int i = 0; i < 10; i++)
			{
				float a = i * Mathf.PI * 2f / 10f;
				ContentCatalog.Part(PrimitiveType.Sphere, "Stone" + i, m, new Vector3(Mathf.Cos(a) * 0.85f, 0f, Mathf.Sin(a) * 0.85f), Quaternion.Euler(0f, i * 37f, 0f),
					new Vector3(0.5f, 0.62f, 0.42f), i % 3 == 0 ? dark : stone);
			}
			ContentCatalog.Part(PrimitiveType.Cylinder, "Basin", m, new Vector3(0f, 0.02f, 0f), Quaternion.identity, new Vector3(1.7f, 0.04f, 1.7f), dark);
			ContentCatalog.Part(PrimitiveType.Cylinder, "Footing", m, new Vector3(0f, -0.4f, 0f), Quaternion.identity, new Vector3(2.05f, 0.42f, 2.05f), dark);
			ContentCatalog.Part(PrimitiveType.Cylinder, "Water", m, new Vector3(0f, 0.16f, 0f), Quaternion.identity, new Vector3(1.55f, 0.02f, 1.55f), water);
			ContentCatalog.AddLabel(root.transform, "Spring", 0.9f);
			return root;
		}

		#endregion

		#region Behaviours' hooks

		internal static string Describe(ObjAction a)
		{
			switch (a.Verb)
			{
				case WeatherVerb:
					string w = (a.Target ?? "").Trim().ToLowerInvariant();
					if (w == "raft" || w == "auto") return "Raft chooses its weather again";
					return WeatherNames.Contains(w) ? "the weather becomes " + w + " for " + Minutes(a.Arg) + " min, for every player" : "weather: '" + a.Target + "' (not clear, calm, fog, waves, rain, storm, snow or raft)";
				case RandomVerb: return "do ONE of the next " + Options(a) + " actions, picked at random (up to a wait)";
				case CountVerb: return (a.Target.Trim().Length > 0 ? "the counter '" + a.Target.Trim() + "' " : "(no counter name) ") + DescribeCount(a.Arg);
				case HealVerb: return DescribeHeal(a.Arg);
			}
			return null;
		}

		internal static string What(ObjCheck c)
		{
			switch (c.Kind)
			{
				case TimeCheck: return DescribeTime(c.Target);
				case WeatherCheck: return "the weather is " + string.Join(" or ", (c.Target ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray());
				case CountCheck: return "the counter '" + c.Target + "' " + DescribeCountCheck(c.Arg);
			}
			return null;
		}

		internal static bool Holds(IslandWorldState.Entry e, ObjCheck c, out bool result)
		{
			result = false;
			switch (c.Kind)
			{
				case TimeCheck: result = TimeNow(c.Target); return true;
				case WeatherCheck: result = WeatherMatches(c.Target, WeatherNow); return true;
				case CountCheck: result = CountMatches(Counter(e, c.Target), c.Arg); return true;
			}
			return false;
		}

		internal static bool RunShared(IslandWorldState.Entry e, ObjAction a)
		{
			switch (a.Verb)
			{
				case WeatherVerb: HostWeather(e, a); return true;
				case CountVerb: HostCount(e, a); return true;
			}
			return false;
		}

		internal static string CheckPlaceholder(string kind)
		{
			switch (kind)
			{
				case TimeCheck: return "day, night, dawn, dusk or 20-6";
				case WeatherCheck: return "rain, storm, calm, snow...";
				case CountCheck: return "counter name";
			}
			return null;
		}

		internal static string CheckHint(string kind)
		{
			switch (kind)
			{
				case TimeCheck: return "The time of day: day, night, dawn (5-8), dusk (18-21), or hours like 20-6 (past midnight) or 12 (12:00-13:00). Several with commas: any of them. Raft's clock, the same for every player";
				case WeatherCheck: return "Raft's weather now: clear, calm, fog, waves, rain, storm (Raft's rain storm or rough snow), snow (Raft's seas have no fog weather). Several with commas: any of them";
				case CountCheck: return "The name of one of the island's counters (the \"count\" action sets it)";
			}
			return null;
		}

		internal static string CheckArgPlaceholder(string kind) { return kind == CountCheck ? ">= 3" : null; }

		internal static string CheckArgHint(string kind) { return kind == CountCheck ? "The counter's value: >= 3 (at least 3), > , <=, <, = 0, != 0; a bare number = at least that (empty = at least 1)" : null; }

		#endregion
	}
}
