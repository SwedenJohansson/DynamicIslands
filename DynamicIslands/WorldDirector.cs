using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// One rule that brings an island into a world: what appears, when, where, and what players are told.
	/// Rules live in world plans (plans\&lt;name&gt;.plan) and in islands (their "bring.rules" setting, e.g. "when my
	/// quest is done, bring island X 600 m north of me"). One line each:
	///   id | what | when | where | message | receiver label
	///   what:  island:&lt;name&gt;   type:&lt;map type&gt;   pool   oneof:&lt;name&gt;, &lt;name&gt;...
	///   when:  start   km:&lt;n&gt;   day:&lt;n&gt;   quest:&lt;ref&gt;   step:&lt;ref&gt;:&lt;n&gt;   zone:&lt;ref&gt;:&lt;zone&gt;   visit:&lt;ref&gt;   rule:&lt;id&gt;
	///   where: ahead:&lt;m&gt;   near:&lt;ref&gt;:&lt;m&gt;:&lt;direction&gt;
	/// A ref names an island in the world: "self" (the island the rule belongs to), the id of the rule that brought
	/// it, or its island name.
	/// </summary>
	public class IntroRule
	{
		public const string Self = "self";
		public static readonly string[] WhatKinds = { "island", "type", "pool", "oneof" };
		public static readonly string[] WhenKinds = { "start", "km", "day", "quest", "step", "zone", "visit", "rule", "signal" };
		public static readonly string[] WhereKinds = { "ahead", "near" };
		public static readonly string[] Directions = { "any", "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

		public string Id = "";
		public string What = "island", WhatArg = "";
		public string When = "start", WhenRef = "", WhenArg = "";
		public string Where = "ahead", WhereRef = "";
		public float Distance = 300f;
		public string Direction = "any";
		public string Message = "", Label = "";

		public IntroRule Clone() { return (IntroRule)MemberwiseClone(); }

		/// <summary>Degrees clockwise from north (+z), or NaN for "any".</summary>
		public static float DirectionAngle(string direction)
		{
			int i = Array.FindIndex(Directions, d => d.Equals((direction ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
			if (i > 0) return (i - 1) * 45f;
			float deg;
			return float.TryParse(direction, NumberStyles.Float, CultureInfo.InvariantCulture, out deg) ? Mathf.Repeat(deg, 360f) : float.NaN;
		}

		/// <summary>"north-east" for an angle clockwise from north.</summary>
		public static string DirectionName(float degrees)
		{
			return Directions[1 + Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 45f) % 8];
		}

		static string Part(string s) { return (s ?? "").Replace("|", "/").Replace(":", "-").Replace("\n", " ").Replace("\r", "").Trim(); }
		static string Text(string s) { return (s ?? "").Replace("|", "/").Replace("\n", " ").Replace("\r", "").Trim(); }
		static string Num(float f) { return f.ToString("0.#", CultureInfo.InvariantCulture); }

		public string ToLine()
		{
			string what = What == "pool" ? "pool" : What + ":" + (What == "oneof" ? Text(WhatArg) : Part(WhatArg));
			string when;
			switch (When)
			{
				case "start": when = "start"; break;
				case "km": case "day": when = When + ":" + Part(WhenArg); break;
				case "step": case "zone": case "signal": when = When + ":" + Part(WhenRef.Length > 0 ? WhenRef : Self) + ":" + Part(WhenArg); break;
				default: when = When + ":" + Part(WhenRef.Length > 0 ? WhenRef : (When == "rule" ? "" : Self)); break;
			}
			string where = Where == "near" ? "near:" + Part(WhereRef.Length > 0 ? WhereRef : Self) + ":" + Num(Distance) + ":" + Part(Direction) : "ahead:" + Num(Distance);
			return string.Join(" | ", new[] { Part(Id), what, when, where, Text(Message), Text(Label) });
		}

		/// <summary>A rule from its line, or null if the line isn't one.</summary>
		public static IntroRule Parse(string line)
		{
			string[] p = (line ?? "").Split('|').Select(x => x.Trim()).ToArray();
			if (p.Length < 4) return null;
			var r = new IntroRule { Id = p[0], Message = p.Length > 4 ? p[4] : "", Label = p.Length > 5 ? p[5] : "" };

			string[] what = p[1].Split(new[] { ':' }, 2);
			r.What = what[0].Trim().ToLowerInvariant();
			r.WhatArg = what.Length > 1 ? what[1].Trim() : "";
			if (!WhatKinds.Contains(r.What)) return null;

			string[] when = p[2].Split(':').Select(x => x.Trim()).ToArray();
			r.When = when[0].ToLowerInvariant();
			if (!WhenKinds.Contains(r.When)) return null;
			if (r.When == "km" || r.When == "day") r.WhenArg = when.Length > 1 ? when[1] : "0";
			else if (r.When != "start")
			{
				r.WhenRef = when.Length > 1 ? when[1] : "";
				r.WhenArg = when.Length > 2 ? string.Join(":", when.Skip(2).ToArray()) : "";
			}

			string[] where = p[3].Split(':').Select(x => x.Trim()).ToArray();
			r.Where = where[0].ToLowerInvariant();
			if (!WhereKinds.Contains(r.Where)) return null;
			float d;
			if (r.Where == "ahead") r.Distance = where.Length > 1 && float.TryParse(where[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 300f;
			else
			{
				r.WhereRef = where.Length > 1 ? where[1] : Self;
				r.Distance = where.Length > 2 && float.TryParse(where[2], NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 600f;
				r.Direction = where.Length > 3 && where[3].Length > 0 ? where[3].ToLowerInvariant() : "any";
			}
			r.Distance = Mathf.Clamp(r.Distance, 50f, 5000f);
			return r;
		}

		public static List<IntroRule> ParseLines(string text)
		{
			return (text ?? "").Split('\n').Select(Parse).Where(r => r != null).ToList();
		}

		public static string ToLines(IEnumerable<IntroRule> rules)
		{
			return string.Join("\n", rules.Select(r => r.ToLine()).ToArray());
		}

		static string RefName(string r) { return r.Length == 0 || r == Self ? "this island" : "'" + r + "'"; }

		public string DescribeWhat()
		{
			switch (What)
			{
				case "type": MapType t = MapTypes.Get(WhatArg); return "a new " + (t != null ? t.Label.ToLowerInvariant() : "'" + WhatArg + "' island");
				case "pool": return "an island from the spawn pool";
				case "oneof": return "one of " + WhatArg;
				default: return "island '" + WhatArg + "'";
			}
		}

		public string DescribeWhen()
		{
			switch (When)
			{
				case "start": return "When the world starts";
				case "km": return "After sailing " + WhenArg + " km";
				case "day": return "On day " + WhenArg;
				case "quest": return "When the quest of " + RefName(WhenRef) + " is done";
				case "step": return "When " + WhenArg + " step(s) of the quest of " + RefName(WhenRef) + " are done";
				case "zone": return "When zone '" + WhenArg + "' of " + RefName(WhenRef) + " fires";
				case "visit": return "When players first reach " + RefName(WhenRef);
				case "rule": return "After rule '" + WhenRef + "'";
				case "signal": return "When the signal '" + WhenArg + "' is sent on " + RefName(WhenRef);
			}
			return When;
		}

		public string DescribeWhere()
		{
			if (Where == "ahead") return Num(Distance) + " m ahead of the raft";
			float a = DirectionAngle(Direction);
			return Num(Distance) + " m " + (float.IsNaN(a) ? "from " : DirectionName(a) + " of ") + RefName(WhereRef);
		}

		public string Describe() { return DescribeWhen() + ": bring " + DescribeWhat() + ", " + DescribeWhere(); }
	}

	/// <summary>
	/// A world plan: which islands a world gets, when and where (its rules), and whether random islands keep
	/// appearing while sailing as well. Chosen when a world is created (Raft's New Game box) or with WorldPlan.
	/// Stored as Mods\DynamicIslands\plans\&lt;name&gt;.plan (text). "Random islands" and "No custom islands" are built in.
	/// </summary>
	public class WorldPlan
	{
		public const string RandomName = "Random islands", NoneName = "No custom islands", Extension = ".plan";

		public string Name = "", Description = "";
		/// <summary>Random islands from the spawn pool appear while sailing (today's behaviour), as well as the rules.</summary>
		public bool Random = true;
		public List<IntroRule> Rules = new List<IntroRule>();

		public bool BuiltIn { get { return IsBuiltIn(Name); } }
		public static bool IsBuiltIn(string name) { return name.Equals(RandomName, StringComparison.OrdinalIgnoreCase) || name.Equals(NoneName, StringComparison.OrdinalIgnoreCase); }

		public static string Folder { get { return Path.Combine(DynamicIslands.assetpath, "plans"); } }
		public static string PathFor(string name) { return Path.Combine(Folder, name + Extension); }

		/// <summary>The built-in plans first, then the saved ones.</summary>
		public static List<string> All()
		{
			var names = new List<string> { RandomName, NoneName };
			try
			{
				if (Directory.Exists(Folder))
					names.AddRange(Directory.GetFiles(Folder, "*" + Extension).Select(Path.GetFileNameWithoutExtension).Where(n => !IsBuiltIn(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not list world plans: " + e.Message); }
			return names;
		}

		/// <summary>A plan by name, or null if there's no such plan.</summary>
		public static WorldPlan Load(string name)
		{
			name = (name ?? "").Trim();
			if (name.Equals(RandomName, StringComparison.OrdinalIgnoreCase)) return new WorldPlan { Name = RandomName, Description = "Islands appear by chance while you sail (spawnpool.txt)", Random = true };
			if (name.Equals(NoneName, StringComparison.OrdinalIgnoreCase)) return new WorldPlan { Name = NoneName, Description = "Only islands you spawn yourself", Random = false };
			string path = PathFor(name);
			if (name.Length == 0 || !File.Exists(path)) return null;
			try { return Parse(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read world plan '" + name + "': " + e.Message); return null; }
		}

		public static WorldPlan Parse(string name, string text)
		{
			var plan = new WorldPlan { Name = name, Random = false };
			foreach (string raw in (text ?? "").Split('\n'))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#")) continue;
				int eq = line.IndexOf('=');
				if (eq <= 0) continue;
				string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
				switch (key)
				{
					case "description": plan.Description = value; break;
					case "random": plan.Random = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
					case "rule":
						IntroRule r = IntroRule.Parse(value);
						if (r != null) plan.Rules.Add(r);
						else Debug.LogWarning("[CUSTOM ISLANDS] World plan '" + name + "': ignoring a bad rule: " + value);
						break;
				}
			}
			// Rules need distinct ids (other rules and the world's state refer to them)
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < plan.Rules.Count; i++)
			{
				if (plan.Rules[i].Id.Length == 0) plan.Rules[i].Id = "rule" + (i + 1);
				while (!seen.Add(plan.Rules[i].Id)) plan.Rules[i].Id += "b";
			}
			return plan;
		}

		public const string Help =
@"# Custom Islands world plan: which islands a world gets, when and where.
# Choose it when creating a world (Raft's New Game box), or in a world with: WorldPlan <name>
#
# description = shown when choosing the plan
# random = on|off   (islands from spawnpool.txt also appear by chance while sailing)
# rule = id | what | when | where | message | receiver label
#   what:  island:<saved island>   type:<map type>   pool   oneof:<island>, <island>, ...
#   when:  start   km:<km sailed>   day:<day>   quest:<ref>   step:<ref>:<steps done>
#          zone:<ref>:<zone name>   visit:<ref>   rule:<rule id>
#   where: ahead:<metres>   near:<ref>:<metres>:<direction>   (any, north, north-east, east, ... or degrees)
#   <ref> is an island in the world: the id of the rule that brought it, or its island name.
#   The message is shown to every player when the island appears; the label is its name on the Receiver.
";

		public string ToText()
		{
			var lines = new List<string> { Help, "description = " + (Description ?? "").Replace("\n", " "), "random = " + (Random ? "on" : "off"), "" };
			lines.AddRange(Rules.Select(r => "rule = " + r.ToLine()));
			return string.Join("\r\n", lines.ToArray()) + "\r\n";
		}

		public void Save()
		{
			Directory.CreateDirectory(Folder);
			File.WriteAllText(PathFor(Name), ToText());
		}
	}

	/// <summary>
	/// Settings of saved island files, read without spawning them (for islands that are far away and unloaded):
	/// their island settings (quests, rules) and zone names. Cached by file time.
	/// </summary>
	public static class IslandCache
	{
		class Info
		{
			public DateTime Time;
			public Dictionary<string, string> Props;
			public List<string> Zones;
		}

		static readonly Dictionary<string, Info> cache = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);

		static Info Get(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			string path = IslandSpawner.PathFor(name);
			try
			{
				if (!File.Exists(path)) return null;
				DateTime t = File.GetLastWriteTimeUtc(path);
				Info info;
				if (cache.TryGetValue(name, out info) && info.Time == t) return info;
				IslandFile f = IslandFile.Load(path);
				info = new Info { Time = t, Props = f.Props, Zones = f.Objects.Where(o => o.Name == ContentCatalog.TriggerZone).Select(o => ObjectProps.Get(o.Props, ObjectProps.ZoneId)).ToList() };
				cache[name] = info;
				return info;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read island '" + name + "': " + e.Message); return null; }
		}

		/// <summary>The island's own settings (empty if the file is missing).</summary>
		public static Dictionary<string, string> Props(string name)
		{
			Info i = Get(name);
			return i != null ? i.Props : new Dictionary<string, string>();
		}

		/// <summary>Settings of a world's island: from the spawned island if it's loaded, else from its file.</summary>
		public static Dictionary<string, string> PropsOf(IslandWorldState.Entry e)
		{
			if (e == null) return new Dictionary<string, string>();
			IslandSettings s = e.Root != null ? e.Root.GetComponent<IslandSettings>() : null;
			return s != null ? s.Props : Props(e.Name);
		}

		/// <summary>Place of the trigger zone with this name among the island's zones, or -1.</summary>
		public static int ZoneOrdinal(string name, string zoneId)
		{
			Info i = Get(name);
			return i == null ? -1 : i.Zones.FindIndex(z => z.Equals((zoneId ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
		}
	}

	/// <summary>
	/// The world director (host only): brings islands into the world by rules - the world's plan and the rules of the
	/// islands already in it - when their moment comes (world start, km sailed, a day, a quest or quest step done, a
	/// zone fired, an island first visited, another rule done). Conditions are checked against the world's state
	/// every second, so events from clients (quests, zones) count as soon as they reach the host, and nothing is
	/// missed across saves. A rule that fired is remembered (plan rules in the world file, island rules in the
	/// island's state) so it never brings its island twice. New islands go through the normal paths: clients get
	/// them (and their files) like any other island, and every player is told where the new island is.
	/// </summary>
	public static class WorldDirector
	{
		/// <summary>Island setting holding the island's own rules (lines, see IntroRule).</summary>
		public const string IslandRulesKey = "bring.rules";
		/// <summary>State keys: a player has been at the island; island rule i has fired (RuleKeyBase + i).</summary>
		public const int VisitKey = 0x50000, RuleKeyBase = 0x50001;
		const float TickInterval = 1f, StartDelay = 4f, RetrySeconds = 20f, VisitMargin = 25f;

		/// <summary>The current world's plan.</summary>
		public static string PlanName = WorldPlan.RandomName;
		public static WorldPlan Plan = WorldPlan.Load(WorldPlan.RandomName);
		/// <summary>Ids of the plan's rules that have fired in this world.</summary>
		public static readonly HashSet<string> Done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		/// <summary>Metres the raft has sailed in this world (for "km" rules).</summary>
		public static float Sailed;
		/// <summary>Chosen in Raft's New Game box for the world being created (null = DefaultPlan).</summary>
		public static string PendingPlan;
		/// <summary>The plan new worlds get when none was chosen (defaultPlan in spawnpool.txt).</summary>
		public static string DefaultPlan = WorldPlan.RandomName;

		/// <summary>Raised on the host when a rule brings an island (tests listen).</summary>
		public static event Action<IslandWorldState.Entry, IntroRule> Brought;
		/// <summary>The last "new island" announcement shown on this machine (tests look at it).</summary>
		public static string LastAnnouncement { get; private set; }

		static float nextTick, loadedAt;
		static readonly Dictionary<string, float> retryAt = new Dictionary<string, float>();
		static readonly HashSet<string> warned = new HashSet<string>();

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }
		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [director] " + msg); }

		#region World file

		/// <summary>Before a world's island list is read.</summary>
		internal static void Reset()
		{
			PlanName = WorldPlan.RandomName;
			Plan = null;
			Done.Clear();
			Sailed = 0f;
			retryAt.Clear();
			warned.Clear();
		}

		/// <summary>A "@key=value" line of the world file; false if it isn't ours.</summary>
		internal static bool ReadLine(string key, string value)
		{
			switch (key)
			{
				case "plan": PlanName = value.Trim(); return true;
				case "sailed": float s; if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out s)) Sailed = s; return true;
				case "done": foreach (string id in value.Split(',')) if (id.Trim().Length > 0) Done.Add(id.Trim()); return true;
			}
			return false;
		}

		internal static IEnumerable<string> WriteLines()
		{
			yield return "@plan=" + PlanName;
			yield return "@sailed=" + Sailed.ToString("F0", CultureInfo.InvariantCulture);
			if (Done.Count > 0) yield return "@done=" + string.Join(",", Done.ToArray());
		}

		/// <summary>True when the world file is needed for the director's state alone.</summary>
		internal static bool HasState { get { return Done.Count > 0 || !PlanName.Equals(WorldPlan.RandomName, StringComparison.OrdinalIgnoreCase); } }

		/// <summary>The world in the game scene has been set up (a saved world's list read, or a new world started).</summary>
		static bool worldHandled;

		/// <summary>
		/// Raft raises SaveAndLoad.LoadComplete only when it restores a saved world, never for a brand-new one. A new
		/// world is noticed here instead: the first moment in a game with Raft's "new game" flag set (the New Game box
		/// sets it, loading a world clears it), once the raft exists.
		/// </summary>
		static void CheckNewWorld()
		{
			bool isNew = false;
			try { isNew = GameManager.IsInNewGame; } catch { }
			if (!isNew || CustomIslandSpawner.RaftPosition == null || SaveAndLoad.WorldGuid == Guid.Empty) return;
			Log("A new world has started");
			IslandWorldState.OnWorldLoaded();
			try { CreatureSpawner.OnWorldLoaded(); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Creatures in the new world: " + e.Message); }
			worldHandled = true;
		}

		/// <summary>After a world's island list was read (host): a brand-new world gets the plan chosen for it.</summary>
		internal static void OnWorldLoaded()
		{
			worldHandled = true;
			loadedAt = Time.unscaledTime;
			if (!Raft_Network.IsHost) return;
			bool isNew = false;
			try { isNew = GameManager.IsInNewGame && Sailed <= 0f && IslandWorldState.Islands.Count == 0 && Done.Count == 0; } catch { }
			if (isNew)
			{
				string chosen = PendingPlan ?? DefaultPlan;
				PendingPlan = null;
				if (WorldPlan.Load(chosen) == null) { Debug.LogWarning("[CUSTOM ISLANDS] No world plan '" + chosen + "'; using " + WorldPlan.RandomName); chosen = WorldPlan.RandomName; }
				SetPlan(chosen, true);
				Log("New world '" + SaveAndLoad.CurrentGameFileName + "': plan '" + PlanName + "'");
				return;
			}
			Plan = WorldPlan.Load(PlanName);
			if (Plan == null) Debug.LogWarning("[CUSTOM ISLANDS] This world's plan '" + PlanName + "' is missing (Mods\\DynamicIslands\\plans); its rules are paused");
			else if (Plan.Rules.Count > 0) Log("Plan '" + PlanName + "': " + Plan.Rules.Count(r => !Done.Contains(r.Id)) + " of " + Plan.Rules.Count + " rule(s) still to come");
		}

		/// <summary>Host: gives this world a plan (applyRandom: random islands on or off as the plan says).</summary>
		public static bool SetPlan(string name, bool applyRandom)
		{
			WorldPlan plan = WorldPlan.Load(name);
			if (plan == null) return false;
			Plan = plan;
			PlanName = plan.Name;
			if (applyRandom) CustomIslandSpawner.Enabled = plan.Random;
			retryAt.Clear();
			return true;
		}

		#endregion

		#region Runtime

		/// <summary>Every frame from the mod.</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + TickInterval;
			if (!LoadSceneManager.IsGameSceneLoaded) { worldHandled = false; return; }
			if (!worldHandled) CheckNewWorld();
			if (!Raft_Network.IsHost || Time.unscaledTime - loadedAt < StartDelay) return;
			if (!CustomIslandSpawner.RaftPosition.HasValue) return;
			UpdateVisits();
			Evaluate();
		}

		/// <summary>Checks every rule that hasn't fired yet (host; tests call it directly).</summary>
		public static void Evaluate()
		{
			if (Plan != null)
				foreach (IntroRule r in Plan.Rules)
				{
					if (Done.Contains(r.Id)) continue;
					IntroRule rule = r;
					TryRule(rule, null, "plan/" + rule.Id, () => Done.Add(rule.Id));
				}
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands.ToList())
			{
				if (e.Failed || e.WaitingForFile) continue;
				List<IntroRule> rules = RulesOf(e);
				for (int i = 0; i < rules.Count; i++)
				{
					int key = RuleKeyBase + i;
					if (e.State.ContainsKey(key)) continue;
					IslandWorldState.Entry owner = e;
					TryRule(rules[i], e, e.Id + "/" + i, () => owner.State[key] = new ObjectState { Active = false, Yield = 0, Day = Today });
				}
			}
		}

		/// <summary>The island's own rules (from its settings).</summary>
		public static List<IntroRule> RulesOf(IslandWorldState.Entry e) { return RulesFromProps(IslandCache.PropsOf(e)); }

		public static List<IntroRule> RulesFromProps(IDictionary<string, string> props)
		{
			string text;
			return props != null && props.TryGetValue(IslandRulesKey, out text) ? IntroRule.ParseLines(text) : new List<IntroRule>();
		}

		public static void SetRulesInProps(IDictionary<string, string> props, IEnumerable<IntroRule> rules)
		{
			List<IntroRule> list = rules.ToList();
			if (list.Count == 0) props.Remove(IslandRulesKey);
			else props[IslandRulesKey] = IntroRule.ToLines(list);
		}

		static void TryRule(IntroRule r, IslandWorldState.Entry owner, string key, Action markDone)
		{
			IslandWorldState.Entry at;
			if (!Met(r, owner, out at)) return;
			float t;
			if (retryAt.TryGetValue(key, out t) && Time.unscaledTime < t) return;
			string why = Bring(r, owner, at);
			if (why == null) { markDone(); retryAt.Remove(key); return; }
			retryAt[key] = Time.unscaledTime + RetrySeconds;
			if (warned.Add(key + why)) Log("Rule '" + r.Id + "' (" + r.Describe() + ") waits: " + why);
		}

		/// <summary>The islands a ref points at: "self" = the rule's own island, else islands brought by that rule, else by island name.</summary>
		public static List<IslandWorldState.Entry> Refs(string reference, IslandWorldState.Entry owner)
		{
			reference = (reference ?? "").Trim();
			if (reference.Length == 0 || reference.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase))
				return owner != null ? new List<IslandWorldState.Entry> { owner } : new List<IslandWorldState.Entry>();
			var byRule = IslandWorldState.Islands.Where(e => e.Rule.Equals(reference, StringComparison.OrdinalIgnoreCase)).ToList();
			return byRule.Count > 0 ? byRule : IslandWorldState.Islands.Where(e => e.HostName.Equals(reference, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		static float Number(string s)
		{
			float f;
			return float.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0f;
		}

		/// <summary>Whether the rule's moment has come; at = the island where it happened (if any).</summary>
		public static bool Met(IntroRule r, IslandWorldState.Entry owner, out IslandWorldState.Entry at)
		{
			at = null;
			switch (r.When)
			{
				case "start": return true;
				case "km": return Sailed >= Number(r.WhenArg) * 1000f;
				case "day": return Today >= Number(r.WhenArg);
				case "rule":
					if (Done.Contains(r.WhenRef)) return true;
					// An island's rule may wait for another of its own rules
					if (owner != null)
					{
						int i = RulesOf(owner).FindIndex(x => x.Id.Equals(r.WhenRef, StringComparison.OrdinalIgnoreCase));
						return i >= 0 && owner.State.ContainsKey(RuleKeyBase + i);
					}
					return false;
			}
			foreach (IslandWorldState.Entry e in Refs(r.WhenRef, owner))
				if (Happened(r, e)) { at = e; return true; }
			return false;
		}

		static bool Happened(IntroRule r, IslandWorldState.Entry e)
		{
			switch (r.When)
			{
				case "quest":
					int steps = IslandQuest.From(IslandCache.PropsOf(e)).Steps.Count;
					return steps > 0 && QuestTracker.StepOf(e) >= steps;
				case "step": return QuestTracker.StepOf(e) >= Mathf.Max(1, (int)Number(r.WhenArg));
				case "zone":
					int ordinal = IslandCache.ZoneOrdinal(e.Name, r.WhenArg);
					return ordinal >= 0 && ContentState.IsUsed(e, TriggerZone.KeyBase + ordinal);
				case "visit": return e.State.ContainsKey(VisitKey);
				case "signal": return e.State.ContainsKey(Behaviours.SignalKey(r.WhenArg));
			}
			return false;
		}

		/// <summary>Host: marks islands a player has come to (for "visit" rules; kept with the island's state).</summary>
		static void UpdateVisits()
		{
			List<Vector3> players = UnityEngine.Object.FindObjectsOfType<Network_Player>().Where(p => p != null).Select(p => p.transform.position).ToList();
			if (players.Count == 0) return;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.State.ContainsKey(VisitKey) || e.Failed) continue;
				float r = CustomIslandSpawner.LandRadius(e.Name);
				if (r < 0f) continue;
				if (players.Any(p => new Vector2(p.x - e.Position.x, p.z - e.Position.z).magnitude < r + VisitMargin))
				{
					e.State[VisitKey] = new ObjectState { Active = true, Yield = 0, Day = Today };
					Log("Players reached '" + e.HostName + "'");
				}
			}
		}

		#endregion

		#region Bringing an island

		/// <summary>Brings the rule's island; null when it's on its way, else why not (yet).</summary>
		static string Bring(IntroRule r, IslandWorldState.Entry owner, IslandWorldState.Entry at)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue) return "no raft";
			var rnd = new System.Random(Guid.NewGuid().GetHashCode());
			string name = null;
			MapType type = null;
			switch (r.What)
			{
				case "island":
					name = r.WhatArg.Trim();
					if (!File.Exists(IslandSpawner.PathFor(name))) return "there is no saved island '" + name + "'";
					break;
				case "oneof":
					var names = r.WhatArg.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0 && File.Exists(IslandSpawner.PathFor(n))).ToList();
					if (names.Count == 0) return "none of the islands '" + r.WhatArg + "' exist";
					// Islands not in the world yet first
					var fresh = names.Where(n => !IslandWorldState.Islands.Any(e => e.HostName.Equals(n, StringComparison.OrdinalIgnoreCase))).ToList();
					if (fresh.Count > 0) names = fresh;
					name = names[rnd.Next(names.Count)];
					break;
				case "pool":
					name = CustomIslandSpawner.PickFromPool();
					if (name == null) return "the spawn pool is empty";
					if (name == CustomIslandSpawner.GeneratedEntry) type = MapTypes.Get("random");
					else if (name.StartsWith(CustomIslandSpawner.TypePrefix, StringComparison.OrdinalIgnoreCase))
					{
						type = MapTypes.Get(name.Substring(CustomIslandSpawner.TypePrefix.Length));
						if (type == null) return "the spawn pool has an unknown map type '" + name + "'";
					}
					break;
				case "type":
					type = MapTypes.Get(r.WhatArg);
					if (type == null) return "there is no map type '" + r.WhatArg + "'";
					break;
			}

			IslandGenSettings gen = null;
			float elevation, radius;
			if (type != null)
			{
				gen = MapTypes.Roll(type, rnd, out elevation);
				name = MapTypes.FileName(type, gen);
				radius = MapTypes.EstimatedRadius(gen);
			}
			else
			{
				radius = CustomIslandSpawner.LandRadius(name);
				if (radius < 0f) return "island '" + name + "' can't be read";
				elevation = CustomIslandSpawner.Elevation(name);
			}

			string why;
			Vector3? spot = Place(r, owner, at, radius, elevation, out why);
			if (!spot.HasValue) return why;

			IslandWorldState.Entry entry = IslandWorldState.Add(name, spot.Value, null, false);
			entry.Rule = r.Id;
			entry.Label = LabelFor(r, name, type);
			entry.Loading = true;
			if (gen != null)
			{
				MapType t = type; IslandGenSettings s = gen; float el = elevation; string n = name;
				CustomIslandSpawner.CacheSize(name, radius, elevation);
				DynamicIslands.instance.StartCoroutine(CustomIslandSpawner.GenerateAndSpawn(() => MapTypes.Create(t, s, el, n), name, entry));
			}
			else
			{
				IslandNetwork.BroadcastAdded(entry);
				DynamicIslands.instance.StartCoroutine(DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true, entry));
			}
			Vector3 raft = CustomIslandSpawner.RaftPosition.Value;
			Log("Rule '" + r.Id + "'" + (owner != null ? " of '" + owner.HostName + "'" : "") + ": bringing '" + name + "' " +
				new Vector2(spot.Value.x - raft.x, spot.Value.z - raft.z).magnitude.ToString("F0") + " m from the raft (" + r.Describe() + ")");
			Announce(entry, r, type);
			if (Brought != null) try { Brought(entry, r); } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Director listener: " + e.Message); }
			return null;
		}

		/// <summary>The name shown on the Receiver: the rule's label, else the island's own name.</summary>
		static string LabelFor(IntroRule r, string name, MapType type)
		{
			if (r.Label.Trim().Length > 0) return r.Label.Trim();
			string title = type == null ? ObjectProps.Get(IslandCache.Props(name), IslandProps.Title) : "";
			return title;
		}

		/// <summary>Where the island goes: clear of the raft, the other custom islands and Raft's own islands.</summary>
		static Vector3? Place(IntroRule r, IslandWorldState.Entry owner, IslandWorldState.Entry at, float radius, float elevation, out string why)
		{
			why = null;
			Vector3 raft = CustomIslandSpawner.RaftPosition.Value;
			var reasons = new List<string>();
			if (r.Where == "near")
			{
				IslandWorldState.Entry near = r.WhereRef.Length == 0 || r.WhereRef.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase)
					? (owner ?? at) : Refs(r.WhereRef, owner).FirstOrDefault();
				if (near == null && at != null) near = at;
				if (near == null) { why = "there is no island '" + r.WhereRef + "' in the world to place it near"; return null; }
				float baseRadius = Mathf.Max(0f, CustomIslandSpawner.LandRadius(near.Name));
				float distance = Mathf.Max(r.Distance, baseRadius + radius + CustomIslandSpawner.Clearance);
				float angle = IntroRule.DirectionAngle(r.Direction);
				bool any = float.IsNaN(angle);
				if (any) angle = (float)new System.Random(Guid.NewGuid().GetHashCode()).NextDouble() * 360f;
				foreach (Vector2 o in Candidates(any))
				{
					float a = angle + o.x;
					Vector3 c = near.Position + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0f, Mathf.Cos(a * Mathf.Deg2Rad)) * distance * o.y;
					c.y = elevation;
					string no = CustomIslandSpawner.Rejects(c, radius, raft, false, 0f);
					if (no == null) return c;
					reasons.Add(no);
				}
			}
			else
			{
				Vector3 dir = CustomIslandSpawner.SailDirection();
				float distance = Mathf.Max(r.Distance, radius + CustomIslandSpawner.Clearance);
				foreach (Vector2 o in Candidates(false).Where(o => Mathf.Abs(o.x) <= 60f))
				{
					Vector3 c = raft + Quaternion.Euler(0, o.x, 0) * dir * distance * o.y;
					c.y = elevation;
					string no = CustomIslandSpawner.Rejects(c, radius, raft, false, 0f);
					if (no == null) return c;
					reasons.Add(no);
				}
			}
			why = "no free spot (" + string.Join("; ", reasons.Distinct().Take(3).ToArray()) + ")";
			return null;
		}

		/// <summary>
		/// Places to try around the wanted spot, best first: (angle off the wanted direction, distance factor). Going a
		/// bit further out costs less than turning away from the direction the builder chose. Raft packs its own
		/// islands densely, so a big island often needs a few tries.
		/// </summary>
		static IEnumerable<Vector2> Candidates(bool anyDirection)
		{
			float[] offsets = anyDirection ? Enumerable.Range(0, 24).Select(i => i * 15f).ToArray() : new[] { 0f, 8f, -8f, 16f, -16f, 25f, -25f, 35f, -35f, 45f, -45f, 60f, -60f, 80f, -80f };
			float[] factors = { 1f, 1.1f, 1.2f, 1.35f, 1.5f, 1.7f, 2f, 2.4f };
			return offsets.SelectMany(a => factors.Select(f => new Vector2(a, f)))
				.OrderBy(v => (anyDirection ? 0f : Mathf.Abs(v.x) / 20f) + (v.y - 1f) * 2.5f)
				.ToList();
		}

		#endregion

		#region Announcing

		/// <summary>Host: tells every player (and itself) that an island appeared, with where it is.</summary>
		static void Announce(IslandWorldState.Entry e, IntroRule r, MapType type)
		{
			string title = e.Label.Length > 0 ? e.Label : type != null ? type.Label : "A new island";
			IslandNetwork.SendAnnounce(e, title, r.Message);
			Show(title, r.Message, e.Position);
		}

		/// <summary>The banner on this machine: the message, and how far and which way the island is from the player.</summary>
		public static void Show(string title, string message, Vector3 position)
		{
			string where = "";
			Network_Player p = RAPI.GetLocalPlayer();
			if (p != null)
			{
				Vector2 d = new Vector2(position.x - p.transform.position.x, position.z - p.transform.position.z);
				float angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
				where = "About " + (Mathf.Round(d.magnitude / 10f) * 10f).ToString("F0") + " m to the " + IntroRule.DirectionName(angle) + (CustomIslandSpawner.ShowOnReceiver ? " (on your Receiver)" : "");
			}
			string text = message.Trim().Length > 0 ? message.Trim() + (where.Length > 0 ? "\n" + where : "") : where;
			LastAnnouncement = title + " | " + text.Replace("\n", " | ");
			IslandInfo.Show(title, "", text);
			Debug.Log("[CUSTOM ISLANDS] New island: " + LastAnnouncement);
		}

		#endregion

		#region Commands

		/// <summary>What the current world's plan is and where its rules stand.</summary>
		public static string Describe()
		{
			var lines = new List<string> { "World plan: " + PlanName + (Plan == null ? " (missing!)" : "") + "; random islands while sailing " + (CustomIslandSpawner.Enabled ? "on" : "off") +
				"; sailed " + (Sailed / 1000f).ToString("F1", CultureInfo.InvariantCulture) + " km, day " + Today };
			if (Plan != null)
				foreach (IntroRule r in Plan.Rules)
					lines.Add("  [" + (Done.Contains(r.Id) ? "done" : "    ") + "] " + r.Id + ": " + r.Describe());
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				List<IntroRule> rules = RulesOf(e);
				for (int i = 0; i < rules.Count; i++)
					lines.Add("  [" + (e.State.ContainsKey(RuleKeyBase + i) ? "done" : "    ") + "] " + e.HostName + "/" + rules[i].Id + ": " + rules[i].Describe());
			}
			lines.Add("Plans: " + string.Join(", ", WorldPlan.All().ToArray()) + "  (WorldPlan <name> changes this world's plan)");
			return string.Join("\n", lines.ToArray());
		}

		#endregion
	}

	/// <summary>
	/// Raft places its own islands (chunk points) as the raft sails into new sea. Custom islands can be far ahead of
	/// the raft (a plan's "600 m north of the camp"), where Raft hasn't placed anything yet: without this, one of
	/// Raft's islands could later appear inside ours. Here Raft's check for a free spot also keeps clear of them.
	/// </summary>
	[HarmonyPatch(typeof(ChunkManager), "DoesPointFit")]
	static class ChunkPointFitPatch
	{
		static void Postfix(ChunkPoint pointToCheck, ref bool __result)
		{
			if (!__result || pointToCheck == null) return;
			try
			{
				bool wreck = pointToCheck.rule != null && pointToCheck.rule.name.IndexOf("FloatingRaft", StringComparison.OrdinalIgnoreCase) >= 0;
				float overlap = pointToCheck.rule != null ? pointToCheck.rule.collisionOverlapRadius : 150f;
				Vector3 p = pointToCheck.worldPosition;
				foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
				{
					float r = CustomIslandSpawner.LandRadius(e.Name);
					if (r < 0f) continue;
					if (new Vector2(p.x - e.Position.x, p.z - e.Position.z).magnitude < (wreck ? 20f : overlap) + r)
					{
						__result = false;
						return;
					}
				}
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Chunk point check: " + ex.Message); }
		}
	}
}
