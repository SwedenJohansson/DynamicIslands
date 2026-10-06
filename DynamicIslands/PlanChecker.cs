using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Check in the World Plans window: goes through a plan (or an island's own rules) the way a world will play it and
	/// says what can't work, what may not work as meant, and what is good to know - each with the rule, why and how to fix
	/// it. It looks inside the islands too: their quests step by step (is there a zone, note, chest or creature of that
	/// name for each step?), their zones and signals, and, for new map-type islands, a sample island of that type.
	/// </summary>
	public static class PlanChecker
	{
		public enum Level { Problem, Warning, Tip }

		public class Finding
		{
			/// <summary>The rule's index (-1 = the plan as a whole).</summary>
			public int Rule = -1;
			public Level Level;
			public string Text = "", Fix = "";
			public Finding(int rule, Level level, string text, string fix = "") { Rule = rule; Level = level; Text = text; Fix = fix ?? ""; }
		}

		/// <summary>What an island has that rules and quests can point at.</summary>
		public class Facts
		{
			public string Name = "";
			/// <summary>A sample of a map type (a new island made in each world), not a saved file.</summary>
			public bool Sample;
			public IslandQuest Quest = new IslandQuest();
			public readonly HashSet<string> Zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				Notes = new HashSet<string>(StringComparer.OrdinalIgnoreCase), Chests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public readonly Dictionary<string, int> Creatures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			public readonly HashSet<string> CreaturesComeBack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public int NoteCount, ChestCount, RaftCrates, Objects;
			/// <summary>Journal pages the island can give: notes with text (an empty note gives none) and "journal" actions.</summary>
			public int PageCount;
			/// <summary>The island's own rules: plan rules may point at the islands they bring by their names.</summary>
			public List<IntroRule> Rules = new List<IntroRule>();
			public string AllText = ""; // (every setting's value: to see whether a story item is given anywhere)
			/// <summary>Story items the island's checks want (has/take story:...) and those it gives (loot, zones, give actions,
			/// Raft's quest item pickups) - the plan's order of them (Check).</summary>
			public readonly HashSet<string> NeedsStory = new HashSet<string>(StringComparer.OrdinalIgnoreCase), GivesStory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public string Describe { get { return Sample ? "a new " + Name + " island (checked on a sample of that map type)" : "'" + Name + "'"; } }
		}

		#region Facts about islands

		static readonly Dictionary<string, KeyValuePair<DateTime, Facts>> saved = new Dictionary<string, KeyValuePair<DateTime, Facts>>(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<string, Facts> samples = new Dictionary<string, Facts>(StringComparer.OrdinalIgnoreCase);

		public static Facts FromFile(IslandFile f, string name, bool sample)
		{
			var x = new Facts { Name = name, Sample = sample, Quest = IslandQuest.From(f.Props), Objects = f.Objects.Count, Rules = WorldDirector.RulesFromProps(f.Props) };
			var text = new System.Text.StringBuilder();
			foreach (var kv in f.Props) if (kv.Key != StoryItems.Key) text.Append(kv.Value).Append('\n');
			// (creatures come back by default, after the island's regrow days - unless the island says never)
			int regrow;
			bool islandNever = int.TryParse(ObjectProps.Get(f.Props, IslandProps.RegrowDays), out regrow) && regrow <= 0;
			foreach (IslandObject o in f.Objects)
			{
				IDictionary<string, string> p = o.Props ?? new Dictionary<string, string>();
				if (o.Name == ContentCatalog.TriggerZone) { string z = ObjectProps.Get(p, ObjectProps.ZoneId); if (z.Length > 0) x.Zones.Add(z); }
				if (ObjectProps.IsNote(o.Name, p))
				{
					x.NoteCount++;
					string t = ObjectProps.Get(p, ObjectProps.NoteTitle); if (t.Length > 0) x.Notes.Add(t);
					if (ObjectProps.Get(p, ObjectProps.NoteText).Trim().Length > 0) x.PageCount++;
				}
				if (ObjectProps.IsLoot(o.Name, p)) { x.ChestCount++; string t = ObjectProps.Get(p, ObjectProps.NoteTitle); if (t.Length > 0) x.Chests.Add(t); }
				if (o.Name == PlaceableCatalog.RaftCrate) x.RaftCrates++;
				ContentCatalog.CreatureKind k = ContentCatalog.CreatureOf(o.Name);
				if (k != null)
				{
					int n; if (!int.TryParse(ObjectProps.Get(p, ObjectProps.CreatureCount), out n) || n < 1) n = 1;
					int had; x.Creatures.TryGetValue(k.Label, out had); x.Creatures[k.Label] = had + n;
					if (ObjectProps.Respawns(p) && !islandNever) x.CreaturesComeBack.Add(k.Label);
				}
				foreach (var kv in p)
				{
					text.Append(kv.Value).Append('\n');
					if (kv.Key.StartsWith(BehaviourProps.EventPrefix) || kv.Key.StartsWith(BehaviourProps.ElsePrefix))
						foreach (ObjAction a in ObjAction.ParseLines(kv.Value))
						{
							if (a.Verb == "signal" && a.Arg.Trim().Length > 0) x.Signals.Add(a.Arg.Trim());
							if (a.Verb == "journal") x.PageCount++;
						}
				}
			}
			foreach (var kv in f.Props.Where(kv => kv.Key.StartsWith(BehaviourProps.EventPrefix) || kv.Key.StartsWith(BehaviourProps.ElsePrefix)))
				foreach (ObjAction a in ObjAction.ParseLines(kv.Value))
				{
					if (a.Verb == "signal" && a.Arg.Trim().Length > 0) x.Signals.Add(a.Arg.Trim());
					if (a.Verb == "journal") x.PageCount++;
				}
			x.AllText = text.ToString();
			// Story items: wanted by checks, given by everything else (loot, zone items, give actions) and by Raft's pickups
			var sources = new List<KeyValuePair<string, string>>(f.Props.Where(kv => kv.Key != StoryItems.Key));
			foreach (IslandObject o in f.Objects)
			{
				if (o.Props != null) sources.AddRange(o.Props);
				if (QuestItemPickups.IsModel(o.Name)) { string id = QuestItemPickups.StoryId(o.Name); if (id != null) x.GivesStory.Add(id); }
			}
			foreach (var kv in sources)
			{
				bool check = kv.Key.StartsWith(BehaviourProps.CheckPrefix);
				foreach (string line in (kv.Value ?? "").Split('\n'))
				{
					if (check)
					{
						System.Text.RegularExpressions.Match m = NeedRx.Match(line.Trim());
						if (m.Success) x.NeedsStory.Add(m.Groups[1].Value);
					}
					else foreach (System.Text.RegularExpressions.Match m in GiveRx.Matches(line)) x.GivesStory.Add(m.Groups[1].Value);
				}
			}
			return x;
		}

		static readonly System.Text.RegularExpressions.Regex NeedRx = new System.Text.RegularExpressions.Regex(@"^(?:has|take)\|" + StoryItems.Prefix + @"([^|;*\s]+)");
		static readonly System.Text.RegularExpressions.Regex GiveRx = new System.Text.RegularExpressions.Regex(StoryItems.Prefix + @"([^|;*\s]+)");

		/// <summary>A saved island's facts (read again when its file changed), or null if there is no such island.</summary>
		public static Facts Saved(string name)
		{
			if (string.IsNullOrEmpty(name)) return null;
			string path = IslandSpawner.PathFor(name);
			if (!File.Exists(path)) return null;
			try
			{
				DateTime t = File.GetLastWriteTimeUtc(path);
				KeyValuePair<DateTime, Facts> had;
				if (saved.TryGetValue(name, out had) && had.Key == t) return had.Value;
				Facts f = FromFile(IslandFile.Load(path), name, false);
				saved[name] = new KeyValuePair<DateTime, Facts>(t, f);
				return f;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Check: could not read '" + name + "': " + e.Message); return null; }
		}

		/// <summary>A map type's facts from a sample island of it (made once: deep = allowed to make it now), or null.</summary>
		public static Facts Sample(string type, bool deep)
		{
			Facts f;
			if (samples.TryGetValue(type ?? "", out f)) return f;
			if (!deep || !PlaceableCatalog.IsBuilt) return null;
			MapType t = MapTypes.Get(type);
			if (t == null) return null;
			try
			{
				float elevation;
				IslandGenSettings s = MapTypes.Roll(t, new System.Random(12345), out elevation);
				f = FromFile(MapTypes.Create(t, s, elevation, "check-" + t.Name), t.Label.ToLowerInvariant(), true);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Check: could not make a sample '" + type + "' island: " + e.Message); f = null; }
			samples[type] = f;
			return f;
		}

		#endregion

		#region The check

		class Ctx
		{
			public WorldPlan Plan;
			public bool IslandMode, Deep;
			public Facts Own; // (island mode: the island being edited)
			public List<Finding> Out = new List<Finding>();
			public HashSet<string> SavedNames;
			/// <summary>Rules on the islands the plan brings (and on the islands those bring), by name, with their island: a
			/// world finds the island one of them brought by the rule's name, as it does a plan rule's.</summary>
			public Dictionary<string, KeyValuePair<IntroRule, Facts>> IslandRules = new Dictionary<string, KeyValuePair<IntroRule, Facts>>(StringComparer.OrdinalIgnoreCase);
			public void Add(int rule, Level level, string text, string fix = "") { Out.Add(new Finding(rule, level, text, fix)); }
		}

		/// <summary>The rules of the islands a plan brings, followed through what those rules bring (not the plan's own names).</summary>
		static void FindIslandRules(Ctx c)
		{
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var queue = new Queue<Facts>(c.Plan.Rules.SelectMany(r => Brings(c, r)));
			while (queue.Count > 0)
			{
				Facts f = queue.Dequeue();
				if (f == null || !seen.Add((f.Sample ? "type:" : "") + f.Name)) continue;
				foreach (IntroRule ir in f.Rules)
				{
					int same = ir.Id.Length > 0 ? c.Plan.Rules.FindIndex(x => x.Id.Equals(ir.Id, StringComparison.OrdinalIgnoreCase)) : -1;
					// (a world finds an island by the name of the rule that brought it, any rule's: the two are mixed up)
					if (same >= 0 && !f.Sample)
						c.Add(same, Level.Warning, R(c, same) + " has the same name as a rule of " + f.Describe + " (its own rules): what waits for or is placed near '" + ir.Id + "' may take the island that one brings.", "Give rule " + (same + 1) + " another name.");
					if (ir.Id.Length == 0 || c.IslandRules.ContainsKey(ir.Id) || same >= 0) continue;
					c.IslandRules[ir.Id] = new KeyValuePair<IntroRule, Facts>(ir, f);
					foreach (Facts g in Brings(c, ir)) queue.Enqueue(g);
				}
			}
		}

		static string R(Ctx c, int i) { IntroRule r = c.Plan.Rules[i]; return "Rule " + (i + 1) + (r.Id.Length > 0 ? " '" + r.Id + "'" : ""); }

		/// <summary>Everything Check finds, problems first. deep: also make sample islands of the map types used (a moment each, once).</summary>
		public static List<Finding> Check(WorldPlan plan, bool islandMode, bool deep, Facts own = null)
		{
			var c = new Ctx { Plan = plan, IslandMode = islandMode, Deep = deep, Own = own, SavedNames = new HashSet<string>(IslandSpawner.ListSavedIslands(), StringComparer.OrdinalIgnoreCase) };
			if (!islandMode) FindIslandRules(c);
			List<IntroRule> rules = plan.Rules;
			if (rules.Count == 0)
			{
				if (islandMode) c.Add(-1, Level.Tip, "This island has no rules of its own: it brings no other islands.", "+ Add a rule, e.g. \"when this island's quest is done, bring a new island near it\".");
				else if (!plan.Random && !plan.RaftStory) c.Add(-1, Level.Problem, "The plan has no rules, random islands are off and Raft's story is off: a world with it gets no islands at all besides Raft's ordinary ones.", "+ Add a rule, or switch random islands on.");
				else c.Add(-1, Level.Tip, "The plan has no rules yet: worlds get " + (plan.Random ? "random islands while sailing" : "no custom islands") + (plan.RaftStory ? " and Raft's story" : "") + ".", "+ Add a rule, or Templates... for a ready-made set.");
			}
			var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < rules.Count; i++)
			{
				IntroRule r = rules[i];
				if (r.Id.Length == 0) c.Add(i, Level.Problem, R(c, i) + " has no name: other rules can't point at its island.", "Type a name in the Name field (e.g. camp).");
				else if (ids.ContainsKey(r.Id)) c.Add(i, Level.Problem, R(c, i) + " has the same name as rule " + (ids[r.Id] + 1) + ": rules that point at '" + r.Id + "' can't tell them apart.", "Give each rule its own name.");
				else ids[r.Id] = i;
			}
			for (int i = 0; i < rules.Count; i++)
			{
				CheckWhen(c, i);
				CheckBring(c, i);
				CheckWhere(c, i);
				CheckTell(c, i);
				if (!islandMode) CheckStory(c, i);
			}
			CheckOrder(c);
			if (!islandMode && rules.Count > 0 && !plan.Random && !rules.Any(r => r.When == "start" || r.When == "km" || r.When == "day" || r.Where == "receiver"))
				c.Add(-1, Level.Warning, "Nothing of the plan comes by itself: no rule starts with the world, a distance or a day, and random islands are off. Every rule waits for another island - so none ever comes.",
					"Let the first rule be \"When the world starts\" (or after a distance / on a day).");
			if (islandMode && rules.Any(x => x.Special)) c.Add(-1, Level.Problem, "An island's own rules can't use Raft's story or the Receiver (a world plan can).", "Use \"Ahead of the raft\" or \"Near an island\", or make these rules in a world plan.");
			foreach (string tip in StoryTips(plan, islandMode)) c.Add(-1, tip.StartsWith("Story:") ? Level.Tip : Level.Tip, tip);
			if (!islandMode) CheckBlueprints(c);
			if (!islandMode) CheckStoryOrder(c);
			return c.Out.OrderBy(f => f.Level).ThenBy(f => f.Rule).ToList();
		}

		/// <summary>
		/// Story items in the plan's order (TODO "Raft's quest items used in the plans"): an island whose doors or chests want
		/// a story item (a keycard, a key...) that neither it nor any island of an earlier rule gives - the player reaches the
		/// lock without the key. Raft's own quest items (raft-...) are given only by their pickups on custom islands.
		/// </summary>
		static void CheckStoryOrder(Ctx c)
		{
			var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < c.Plan.Rules.Count; i++) foreach (Facts f in Brings(c, c.Plan.Rules[i])) all.UnionWith(f.GivesStory);
			for (int i = 0; i < c.Plan.Rules.Count; i++)
			{
				List<Facts> here = Brings(c, c.Plan.Rules[i]).ToList();
				foreach (Facts f in here) given.UnionWith(f.GivesStory);
				foreach (Facts f in here)
					foreach (string need in f.NeedsStory.Where(n => !given.Contains(n)))
						c.Add(i, Level.Warning, f.Describe + " has a lock that wants the story item " + StoryItems.Label(need) + (all.Contains(need) ? ", which only an island of a later rule gives: players get there without it." : ", which no island of the plan gives: it stays locked."),
							"Give it on an earlier island of the plan (a chest's loot, a zone's items, an action, or one of Raft's quest item pickups), or on this island before the lock.");
			}
		}

		/// <summary>Raft's progression (the user, 2026-10-05): every blueprint that lies on Raft's story islands
		/// (raft_blueprints.txt) must be found somewhere in a plan - on the story islands it keeps, or given by its own
		/// islands (quest rewards, chest loot, notes) - or the player can never build it. Says which island of the plan gives
		/// which, in the plan's order, and warns about any never given.</summary>
		static void CheckBlueprints(Ctx c)
		{
			ScrambledBlueprints.Read();
			if (ScrambledBlueprints.OnIslands.Count == 0) return;
			Func<string, string> key = n => { ChunkPointType t = StoryOrder.Parse(n); return t != ChunkPointType.None ? StoryOrder.Key(t) : n; };
			var replaced = new HashSet<string>(c.Plan.Rules.Where(r => r.StoryPlace.StartsWith("instead:")).Select(r => key(r.StoryPlace.Substring(8))), StringComparer.OrdinalIgnoreCase);
			var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (c.Plan.RaftStory)
				foreach (var kv in ScrambledBlueprints.OnIslands)
					foreach (string isl in kv.Value)
						if (!c.Plan.LeaveOut.Any(l => key(l).Equals(key(isl), StringComparison.OrdinalIgnoreCase)) && !replaced.Contains(key(isl))) kept.Add(kv.Key);
			var given = new HashSet<string>(kept, StringComparer.OrdinalIgnoreCase);
			var order = new List<string>();
			var bp = new System.Text.RegularExpressions.Regex(@"Blueprint_[A-Za-z0-9_]+");
			for (int i = 0; i < c.Plan.Rules.Count; i++)
			{
				var mine = new List<string>();
				foreach (Facts f in Brings(c, c.Plan.Rules[i]))
					foreach (System.Text.RegularExpressions.Match m in bp.Matches(f.AllText))
						if (ScrambledBlueprints.OnIslands.ContainsKey(m.Value) && !mine.Contains(m.Value)) mine.Add(m.Value);
				if (mine.Count == 0) continue;
				foreach (string b in mine) given.Add(b);
				order.Add("rule " + (i + 1) + " '" + c.Plan.Rules[i].Id + "': " + string.Join(", ", mine.Select(Pretty).ToArray()));
			}
			var missing = ScrambledBlueprints.OnIslands.Keys.Where(b => !given.Contains(b)).OrderBy(b => b).ToList();
			if (missing.Count > 0)
				c.Add(-1, Level.Warning, "Raft's blueprints never given in this plan: " + string.Join(", ", missing.Select(b => Pretty(b) + " (on " + string.Join("/", ScrambledBlueprints.OnIslands[b].ToArray()) + " in Raft)").ToArray()) +
					". The story islands that carry them are left out or replaced, and no island of the plan gives them - the player can never build these.",
					"Give each as a quest reward, in a chest's loot or from a note on one of the plan's islands, in a sensible order (early tools early, the engine before the long legs) - or keep those story islands.");
			if (order.Count > 0)
				c.Add(-1, Level.Tip, "Raft's blueprints given by the plan's islands, in its order: " + string.Join("; ", order.ToArray()) +
					(kept.Count > 0 ? ". The story islands it keeps give " + kept.Count + " more." : "."));
		}

		static string Pretty(string blueprint) { return blueprint.Replace("Blueprint_", "").Replace("_", " "); }

		/// <summary>The islands a rule's reference means: a rule of the plan (what it brings), this island (self), or a saved island.</summary>
		static List<Facts> RefFacts(Ctx c, string reference, out string what, out bool known, out int ruleIndex)
		{
			what = reference; known = true; ruleIndex = -1;
			var list = new List<Facts>();
			if (reference.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase))
			{
				what = c.IslandMode ? "this island" : "self";
				known = c.IslandMode;
				if (c.Own != null) list.Add(c.Own);
				return list;
			}
			int idx = c.Plan.Rules.FindIndex(x => x.Id.Equals(reference, StringComparison.OrdinalIgnoreCase));
			if (idx >= 0)
			{
				ruleIndex = idx;
				what = "rule " + (idx + 1) + " '" + reference + "' (" + c.Plan.Rules[idx].DescribeWhat() + ")";
				list.AddRange(Brings(c, c.Plan.Rules[idx]));
				return list;
			}
			KeyValuePair<IntroRule, Facts> own;
			if (c.IslandRules.TryGetValue(reference, out own))
			{
				what = "the rule '" + reference + "' of " + own.Value.Describe + " (" + own.Key.DescribeWhat() + ")";
				list.AddRange(Brings(c, own.Key));
				return list;
			}
			if (c.SavedNames.Contains(reference)) { what = "the saved island '" + reference + "'"; Facts f = Saved(reference); if (f != null) list.Add(f); return list; }
			known = false;
			return list;
		}

		/// <summary>The islands a rule may bring (null entries dropped; a spawn pool island is unknown).</summary>
		static IEnumerable<Facts> Brings(Ctx c, IntroRule r)
		{
			if (r.What == "island") { Facts f = Saved(r.WhatArg.Trim()); if (f != null) yield return f; }
			else if (r.What == "oneof") { foreach (string n in r.WhatArg.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)) { Facts f = Saved(n); if (f != null) yield return f; } }
			else if (r.What == "type") { Facts f = Sample(r.WhatArg.Trim(), c.Deep); if (f != null) yield return f; }
		}

		static bool NeedsIsland(string when) { return when == "quest" || when == "step" || when == "zone" || when == "visit" || when == "signal"; }

		static void CheckWhen(Ctx c, int i)
		{
			IntroRule r = c.Plan.Rules[i];
			float num;
			if (r.When == "km" || r.When == "day")
			{
				if (!float.TryParse(r.WhenArg, NumberStyles.Float, CultureInfo.InvariantCulture, out num) || num < 0f)
					c.Add(i, Level.Problem, R(c, i) + " waits " + (r.When == "km" ? "for a distance" : "for a day") + " but has no number (\"" + r.WhenArg + "\").", "Type " + (r.When == "km" ? "the km to sail, e.g. 2" : "the day, e.g. 3") + " in WHEN.");
				// (the game plays them at once: 0 km sailed and day 0 are reached when the world starts)
				else if (num <= 0f)
					c.Add(i, Level.Tip, R(c, i) + " waits for " + (r.When == "km" ? "0 km" : "day 0") + ": that is there when the world starts, so it comes at once - like \"When the world starts\".", "Fine if that is what you want; else type " + (r.When == "km" ? "the km to sail, e.g. 2" : "a later day, e.g. 3") + ".");
				else if (r.When == "km" && num > 30f || r.When == "day" && num > 20f)
					c.Add(i, Level.Tip, R(c, i) + " comes only after " + (r.When == "km" ? num + " km of sailing" : "day " + num) + ": that takes a long time in play - and to test.", "Try the plan with a small number first.");
			}
			if (r.When == "step" && (!int.TryParse(r.WhenArg, out int n) || n < 1))
				c.Add(i, Level.Problem, R(c, i) + " waits for quest steps but the number of steps (\"" + r.WhenArg + "\") isn't a number from 1 up.", "Type how many steps, e.g. 2.");
			if (r.When == "rule")
			{
				if (r.WhenRef.Length == 0) c.Add(i, Level.Problem, R(c, i) + " waits for another rule but doesn't say which.", "Choose the rule after \"rule\" in WHEN (▾ lists them).");
				else if (r.WhenRef.Equals(r.Id, StringComparison.OrdinalIgnoreCase)) c.Add(i, Level.Problem, R(c, i) + " waits for itself: it never comes.", "Choose another rule to wait for.");
				else if (!c.Plan.Rules.Any(x => x.Id.Equals(r.WhenRef, StringComparison.OrdinalIgnoreCase))) c.Add(i, Level.Problem, R(c, i) + " waits for rule '" + r.WhenRef + "', which isn't in the " + (c.IslandMode ? "island's rules" : "plan") + ".", "Pick the rule from the ▾ list, or type its name exactly.");
				return;
			}
			if (!NeedsIsland(r.When)) return;
			if (r.WhenRef.Length == 0)
			{
				if (!c.IslandMode) c.Add(i, Level.Problem, R(c, i) + " waits for something on an island but doesn't say which island.", "Choose the island after \"island\" in WHEN: a rule of this plan (the island it brings) or one of your saved islands.");
				return;
			}
			if (r.WhenRef.Equals(r.Id, StringComparison.OrdinalIgnoreCase)) { c.Add(i, Level.Problem, R(c, i) + " waits for its own island: it can't come before it is there, so it never comes.", "Wait for another rule's island."); return; }
			string what; bool known; int ruleIndex;
			List<Facts> islands = RefFacts(c, r.WhenRef, out what, out known, out ruleIndex);
			if (!known && r.WhenRef.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase)) { c.Add(i, Level.Problem, R(c, i) + " waits for \"self\", which only means something in an island's own rules (the island itself): in a world plan it points at nothing, so the rule never comes.", "Name the island instead: a rule of this plan, or one of your saved islands."); return; }
			if (!known) { c.Add(i, Level.Problem, R(c, i) + " waits for '" + r.WhenRef + "': no rule of this plan and no saved island has that name.", "Pick it from the ▾ list after the island field (it lists the plan's rules and your saved islands)."); return; }
			if (ruleIndex < 0 && !c.IslandMode && !r.WhenRef.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase) && !c.IslandRules.ContainsKey(r.WhenRef) && !c.Plan.Rules.Any(x => IslandNamed(x, r.WhenRef)))
				c.Add(i, c.Plan.Random ? Level.Warning : Level.Problem, R(c, i) + " waits for the saved island '" + r.WhenRef + "', but no rule of this plan brings it. " +
					(c.Plan.Random ? "It only works if that island turns up by chance while sailing (or another plan or an island's rule brings it)." : "Random islands are off, so it never comes into the world - and this rule never fires."),
					"Add a rule that brings '" + r.WhenRef + "', and wait for that rule's name instead.");
			if (ruleIndex >= 0 && c.Plan.Rules[ruleIndex].What == "pool")
				c.Add(i, Level.Tip, R(c, i) + " waits for " + what + ": a random island from the spawn pool, so what it has (a quest, zones) can't be checked here.", "");
			foreach (Facts f in islands) CheckIslandFor(c, i, r.When, r.WhenArg, f, "WHEN");
		}

		/// <summary>Does this rule bring an island of that name (island:, one of oneof:)?</summary>
		static bool IslandNamed(IntroRule x, string name)
		{
			return x.What == "island" && x.WhatArg.Trim().Equals(name, StringComparison.OrdinalIgnoreCase) ||
				x.What == "oneof" && x.WhatArg.Split(',').Any(n => n.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>What a WHEN (or a story "done when") needs of an island: a quest that can be finished, enough steps, the zone, the signal.</summary>
		static void CheckIslandFor(Ctx c, int i, string kind, string arg, Facts f, string part)
		{
			string who = R(c, i) + (part == "STORY" ? " (its STORY \"done when\")" : "");
			if (kind == "quest" || kind == "step")
			{
				if (!f.Quest.Exists) { c.Add(i, Level.Problem, who + " waits for the quest of " + f.Describe + ", which has no quest: it never " + (part == "STORY" ? "counts as done" : "comes") + ".", f.Sample ? "Use \"When players reach an island\" for this map type, or a map type with a quest (camp, treasure, stacks, swamp...)." : "Use \"When players reach an island\", or give '" + f.Name + "' a quest (open it in the editor: Island tab > Edit quest...)."); return; }
				int need = f.Quest.Steps.Count;
				if (kind == "step" && int.TryParse(arg, out int n)) { if (n > need) c.Add(i, Level.Problem, who + " waits for " + n + " quest steps of " + f.Describe + ", but its quest has only " + need + ".", "Wait for at most " + need + " steps."); need = Math.Min(n, need); }
				CheckQuest(c, i, f, need, who);
			}
			else if (kind == "zone")
			{
				if (arg.Trim().Length == 0) c.Add(i, Level.Problem, who + " waits for a trigger zone of " + f.Describe + " but doesn't say which.", "Choose the zone with the ▾ after the zone field.");
				else if (!f.Zones.Contains(arg.Trim())) c.Add(i, Level.Problem, who + " waits for the zone '" + arg + "' of " + f.Describe + ", which has " + (f.Zones.Count == 0 ? "no trigger zones" : "only " + List(f.Zones)) + ".", "Pick the zone from the ▾ list" + (f.Sample ? "" : ", or place a trigger zone named '" + arg + "' on the island") + ".");
			}
			else if (kind == "note")
			{
				int n;
				List<int> notes = f.Sample ? null : IslandCache.NotesOf(f.Name);
				if (!int.TryParse(arg.Trim(), out n)) c.Add(i, Level.Problem, who + " waits for a note of " + f.Describe + " but doesn't say which.", "Choose the note with the ▾ after the note field.");
				else if (notes != null && !notes.Contains(n)) c.Add(i, Level.Problem, who + " waits for note #" + n + " of " + f.Describe + ", which has " + (notes.Count == 0 ? "no notes with a text" : "only notes " + string.Join(", ", notes.Select(x => "#" + x).ToArray())) + ".", "Pick the note from the ▾ list.");
			}
			else if (kind == "signal")
			{
				if (arg.Trim().Length == 0) c.Add(i, Level.Problem, who + " waits for a signal from " + f.Describe + " but doesn't say which.", "Choose the signal with the ▾ after the signal field.");
				else if (!f.Signals.Contains(arg.Trim())) c.Add(i, Level.Problem, who + " waits for the signal '" + arg + "' from " + f.Describe + ", but nothing there sends it" + (f.Signals.Count > 0 ? " (it sends " + List(f.Signals) + ")" : "") + ".", "Pick it from the ▾ list, or give an object a \"send a signal\" action with that name (Behaviour & events...).");
			}
		}

		/// <summary>An island's own quest checked against what the island has (ROADMAP CW6: every generated quest).</summary>
		public static List<Finding> QuestFindings(IslandFile f)
		{
			var c = new Ctx { Plan = new WorldPlan(), IslandMode = true, SavedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
			Facts x = FromFile(f, f.Name ?? "island", false);
			CheckQuest(c, -1, x, x.Quest.Steps.Count, "The island");
			return c.Out;
		}

		/// <summary>The first `steps` steps of an island's quest: can each be done there?</summary>
		static void CheckQuest(Ctx c, int i, Facts f, int steps, string who)
		{
			for (int s = 0; s < steps && s < f.Quest.Steps.Count; s++)
			{
				IslandQuest.Step st = f.Quest.Steps[s];
				string t = st.Target.Trim();
				string head = who + ": step " + (s + 1) + " of the quest of " + f.Describe + " (\"" + st.Describe() + "\")";
				string tail = " - the quest can't be finished, so the rule never " + (who.Contains("STORY") ? "counts as done" : "comes") + ".";
				string fix = f.Sample ? "Choose another map type, or \"When players reach an island\"." : "Open '" + f.Name + "' in the editor and fix its quest (Island tab > Edit quest...), or the object it names.";
				switch (st.Type)
				{
					case "reach":
						if (t.Length > 0 && !f.Zones.Contains(t)) c.Add(i, Level.Problem, head + " needs a trigger zone '" + t + "', which the island doesn't have" + (f.Zones.Count > 0 ? " (it has " + List(f.Zones) + ")" : "") + tail, fix);
						else if (t.Length == 0 && f.Zones.Count == 0) c.Add(i, Level.Problem, head + " needs a trigger zone to walk into, and the island has none" + tail, fix);
						break;
					case "read":
						if (t.Length > 0 && !f.Notes.Contains(t)) c.Add(i, Level.Problem, head + " needs a note titled \"" + t + "\", which the island doesn't have" + (f.Notes.Count > 0 ? " (its notes: " + List(f.Notes) + ")" : "") + tail, fix);
						else if (t.Length == 0 && f.NoteCount == 0) c.Add(i, Level.Problem, head + " needs a note to read, and the island has none" + tail, fix);
						break;
					case "open":
						if (t.Length > 0 && !f.Chests.Contains(t)) c.Add(i, Level.Problem, head + " needs a chest titled \"" + t + "\", which the island doesn't have" + (f.Chests.Count > 0 ? " (its chests: " + List(f.Chests) + ")" : "") + tail, fix);
						else if (t.Length == 0 && f.ChestCount == 0) c.Add(i, Level.Problem, head + " needs a chest to open, and the island has none" + (f.RaftCrates > 0 ? " (its abandoned raft crates don't count: place a chest from Loot & chests)" : "") + tail, fix);
						break;
					case "kill":
					case "catch":
						int have = t.Length > 0 ? (f.Creatures.TryGetValue(t, out int h) ? h : 0) : f.Creatures.Values.Sum();
						if (have == 0) c.Add(i, Level.Problem, head + " needs " + (t.Length > 0 ? t.ToLowerInvariant() + "s" : "creatures") + ", and the island has none" + tail, fix);
						else if (st.Count > have && !(t.Length > 0 ? f.CreaturesComeBack.Contains(t) : f.CreaturesComeBack.Count > 0))
							c.Add(i, Level.Warning, head + " needs " + st.Count + ", but the island has only " + have + " and they don't come back: players can't reach " + st.Count + " there.", "Place more, or set them to come back after some days, or lower the number in the quest.");
						else if (st.Count > have)
							c.Add(i, Level.Tip, head + " needs " + st.Count + ", and the island has " + have + " at a time: players wait for them to come back (after the island's regrow days) to reach " + st.Count + ".", "Fine if that is what you want; else place more or lower the number.");
						// (Raft leaves some kinds out in some game modes - screechers and puffer fish in Creative: the step can't be done there)
						ContentCatalog.CreatureKind kind = t.Length > 0 ? ContentCatalog.Creatures.FirstOrDefault(k => k.Label.Equals(t, StringComparison.OrdinalIgnoreCase)) : null;
						List<string> without = kind != null && have > 0 ? CreatureSpawner.ModesWithout(kind.Type) : new List<string>();
						if (without.Count > 0)
							c.Add(i, Level.Tip, head + " needs " + t.ToLowerInvariant() + "s, and in " + string.Join(" and ", without.ToArray()) + " worlds Raft has none (as on its own islands): there the quest can't be finished.", "Fine for worlds in the other game modes; for every mode, use another creature.");
						break;
					case "collect":
						if (t.Length > 0 && !f.AllText.Contains(StoryItems.Ref(StoryItems.IdOf(t))))
							c.Add(i, Level.Warning, head + " needs the story item " + StoryItems.Label(t) + ", but nothing on the island gives it (no chest, zone or action). It works only if players got it on another island.", "Put it in a chest or a zone's items on this island, or make sure another island gives it first.");
						break;
					case "pages":
						// (a page comes from a note with text, or a "journal" action: an empty note gives none)
						if (st.Target != "all" && f.PageCount < st.Count)
							c.Add(i, Level.Problem, head + " needs " + st.Count + " pages read on the island, and it gives only " + f.PageCount + (f.NoteCount > f.PageCount ? " (" + (f.NoteCount - f.PageCount) + " of its notes have no text: an empty note gives no page)" : "") + tail, fix);
						break;
				}
			}
		}

		static void CheckBring(Ctx c, int i)
		{
			IntroRule r = c.Plan.Rules[i];
			string arg = r.WhatArg.Trim();
			if (r.What == "island")
			{
				if (arg.Length == 0) c.Add(i, Level.Problem, R(c, i) + " brings an island but doesn't say which.", "Choose it with the ▾ after the island field in BRING.");
				else if (!c.SavedNames.Contains(arg)) c.Add(i, Level.Problem, R(c, i) + " brings '" + arg + "', which isn't a saved island.", "Pick one of your islands with ▾, or save the island under that name.");
				else
				{
					Facts f = Saved(arg);
					if (f != null && f.Objects > 12000) c.Add(i, Level.Warning, R(c, i) + " brings '" + arg + "' with " + f.Objects + " objects: big islands take long to appear and can slow the game.", "Thin it out in the editor (the generator keeps islands under 12 000).");
					if (c.Plan.Rules.Where((x, j) => j != i && x.What == "island" && x.WhatArg.Trim().Equals(arg, StringComparison.OrdinalIgnoreCase)).Any())
						c.Add(i, Level.Tip, R(c, i) + " brings '" + arg + "', and another rule brings it too: the world gets two copies of it.", "Fine if that is what you want; else use another island in one of them.");
				}
			}
			else if (r.What == "oneof")
			{
				var names = r.WhatArg.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
				var missing = names.Where(n => !c.SavedNames.Contains(n)).ToList();
				if (names.Count == 0) c.Add(i, Level.Problem, R(c, i) + " picks one island from a list, but the list is empty.", "Add islands with the ▾ after the list.");
				else if (missing.Count == names.Count) c.Add(i, Level.Problem, R(c, i) + " picks one of " + List(names) + ", but none of them is a saved island.", "Add saved islands with the ▾ after the list.");
				else if (missing.Count > 0) c.Add(i, Level.Warning, R(c, i) + " picks one of a list, but " + List(missing) + (missing.Count == 1 ? " isn't a saved island" : " aren't saved islands") + ": only the others can come.", "Take them out of the list, or save islands with those names.");
			}
			else if (r.What == "type")
			{
				if (MapTypes.Get(arg) == null) c.Add(i, Level.Problem, R(c, i) + " brings a map type '" + arg + "', which doesn't exist.", "Choose one with the ▾ after the map type field.");
			}
			else if (r.What == "pool")
			{
				if (WorldIslands.Candidates().Count == 0) c.Add(i, Level.Problem, R(c, i) + " brings a random island from the spawn pool, but the pool is empty.", "Save some islands, or bring a map type instead.");
			}
		}

		static void CheckWhere(Ctx c, int i)
		{
			IntroRule r = c.Plan.Rules[i];
			if (r.Distance > 3000f) c.Add(i, Level.Tip, R(c, i) + " puts its island " + r.Distance.ToString("0") + " m away: far enough that players may not find it" + (r.Label.Length == 0 ? " (it has no Receiver name)" : "") + ".", "A message saying which way helps, and a Receiver name shows it on Raft's Receiver.");
			if (r.Where != "near") return;
			bool happened = r.WhereRef.Length == 0 || r.WhereRef.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase) && !c.IslandMode;
			if (happened)
			{
				if (!c.IslandMode && !NeedsIsland(r.When))
					c.Add(i, Level.Problem, R(c, i) + " is placed near \"the island where it happened\", but " + r.DescribeWhen().ToLowerInvariant() + " happens at no island: it never finds a place.", "Name an island after \"of\" in WHERE (▾ lists them).");
				return;
			}
			if (r.WhereRef.Equals(IntroRule.Self, StringComparison.OrdinalIgnoreCase)) return;
			int idx = c.Plan.Rules.FindIndex(x => x.Id.Equals(r.WhereRef, StringComparison.OrdinalIgnoreCase));
			bool islandRule = idx < 0 && c.IslandRules.ContainsKey(r.WhereRef);
			if (idx < 0 && !islandRule && !c.SavedNames.Contains(r.WhereRef)) { c.Add(i, Level.Problem, R(c, i) + " is placed near '" + r.WhereRef + "': no rule and no saved island has that name.", "Pick the island from the ▾ list after \"of\"."); return; }
			if (idx == i) { c.Add(i, Level.Problem, R(c, i) + " is placed near its own island: it can't be.", "Choose another island after \"of\"."); return; }
			if (idx < 0 && !islandRule && !c.IslandMode && !c.Plan.Rules.Any(x => IslandNamed(x, r.WhereRef)))
				c.Add(i, Level.Warning, R(c, i) + " is placed near the saved island '" + r.WhereRef + "', which no rule of this plan brings: it waits until that island is in the world" + (c.Plan.Random ? " (by chance)" : ", and random islands are off") + ".", "Place it near a rule of this plan instead (its name), or add a rule that brings '" + r.WhereRef + "'.");
		}

		static void CheckTell(Ctx c, int i)
		{
			IntroRule r = c.Plan.Rules[i];
			if (r.Where == "receiver" && r.Label.Length == 0)
				c.Add(i, Level.Tip, R(c, i) + " is found with the Receiver but has no Receiver name: its frequency's note and dot show no name.", "Type a name in TELL \"on the Receiver\".");
			else if (r.Message.Length == 0 && r.Where != "sailing")
				c.Add(i, Level.Tip, R(c, i) + " has no message: players only see that an island appeared and where.", "A line in TELL makes the story (e.g. \"Smoke rises from a small island ahead.\").");
		}

		static void CheckStory(Ctx c, int i)
		{
			IntroRule r = c.Plan.Rules[i];
			// (the user, 2026-10-04: main story islands are found by their coordinates - a frequency on the Receiver)
			if (r.MainStory && r.Where != "receiver")
				c.Add(i, Level.Problem, R(c, i) + " is main story, but it isn't on the Receiver: a main story island needs coordinates (its own frequency) - its tab in Raft's notebook shows them.",
					"WHERE: choose \"On the Receiver\" (or make it a side quest in STORY).");
			if (r.Beside) foreach (Facts f in Brings(c, r)) CheckNotebook(c, i, r, f);
			if (!r.InStory) return;
			bool endsWithUtopia = StoryChain.EndsStory(c.Plan, ChunkPointType.Landmark_Utopia);
			// (Utopia ends Raft's story: it never counts as done, so an island after it never unlocks)
			if (r.StoryPlace.StartsWith("after:") && StoryOrder.Parse(r.StoryPlace.Substring(6)) == ChunkPointType.Landmark_Utopia && endsWithUtopia)
				c.Add(i, Level.Problem, R(c, i) + " comes in the story after Utopia, but Utopia is the end of Raft's story: it never counts as done, so this island never unlocks.", "Place it after Temperance (or in place of Utopia), or leave Utopia out of the story.");
			else if (r.StoryPlace.StartsWith("after:") && StoryOrder.Parse(r.StoryPlace.Substring(6)) == ChunkPointType.None && !c.Plan.Rules.Any(o => o != r && o.InStory && o.Id.Equals(r.StoryPlace.Substring(6), StringComparison.OrdinalIgnoreCase)))
				c.Add(i, endsWithUtopia ? Level.Problem : Level.Warning, R(c, i) + " comes in the story after '" + r.StoryPlace.Substring(6) + "', which isn't in the story: it goes at the end" +
					(endsWithUtopia ? ", after Utopia - which never counts as done, so this island never unlocks." : "."), "Choose its place again in STORY.");
			string kind = r.StoryDone.Split(':')[0], arg = r.StoryDone.Contains(":") ? r.StoryDone.Substring(r.StoryDone.IndexOf(':') + 1) : "";
			foreach (Facts f in Brings(c, r))
			{
				CheckNotebook(c, i, r, f);
				if (kind == "" && !f.Quest.Exists) continue; // (no quest: reaching it counts)
				if (kind == "" || kind == "quest") CheckIslandFor(c, i, "quest", "", f, "STORY");
				else if (kind == "step" || kind == "zone" || kind == "signal" || kind == "note") CheckIslandFor(c, i, kind, arg, f, "STORY");
			}
		}

		/// <summary>Longer than this, a note's text gets small on its paper in Raft's notebook (Raft's own are up to ~350).</summary>
		public const int NotebookNoteLength = 1100;

		/// <summary>A main story island in Raft's notebook (QuestBook): notes for its pages, notes that fit the paper, its story
		/// items' pictures, a tab title that fits.</summary>
		static void CheckNotebook(Ctx c, int i, IntroRule r, Facts f)
		{
			if (f.Sample) return;
			Dictionary<int, KeyValuePair<string, string>> notes = IslandCache.NoteTextsOf(f.Name);
			if (notes.Count == 0)
				c.Add(i, Level.Tip, R(c, i) + " is main story, but '" + f.Name + "' has no notes with a text: its tab in Raft's notebook shows only its intro and quest steps.", "Place notes with a text on the island (they become the tab's pages when read), or write a tab intro (NOTEBOOK).");
			foreach (KeyValuePair<int, KeyValuePair<string, string>> n in notes)
				if (n.Value.Value.Length > NotebookNoteLength)
					c.Add(i, Level.Warning, R(c, i) + ": the note '" + (n.Value.Key.Length > 0 ? n.Value.Key : "#" + n.Key) + "' on '" + f.Name + "' is " + n.Value.Value.Length + " characters long: on its paper in Raft's notebook the writing gets very small.", "Keep notes of main story islands under " + NotebookNoteLength + " characters (split a long one in two).");
			foreach (StoryItemDef d in StoryItems.Of(IslandCache.Props(f.Name)).Where(d => d.Icon.Trim().Length == 0))
				c.Add(i, Level.Tip, R(c, i) + ": the story item '" + d.ShownName + "' of '" + f.Name + "' has no picture: in Raft's 'Found items' it shows an empty slot.", "Give it one of Raft's quest item pictures (Island tab > Story items...).");
			if (r.TabName.Length > 24)
				c.Add(i, Level.Tip, R(c, i) + ": its tab title '" + r.TabName + "' is long - on the tab the writing gets small.", "A short tab title (NOTEBOOK > tab title), up to about 18 letters.");
			if (r.TabColour == 3) c.Add(i, Level.Tip, R(c, i) + ": tab colour 3 isn't one of Raft's (Raft has no third tab picture): it gets a colour by its place.", "Choose a colour from the list (NOTEBOOK).");
		}

		/// <summary>Rules that wait for each other, and rules waiting for a rule that can't work.</summary>
		static void CheckOrder(Ctx c)
		{
			List<IntroRule> rules = c.Plan.Rules;
			Func<int, int> dep = i =>
			{
				IntroRule r = rules[i];
				if (r.When != "rule" && !NeedsIsland(r.When)) return -1;
				return rules.FindIndex(x => x.Id.Length > 0 && x.Id.Equals(r.WhenRef, StringComparison.OrdinalIgnoreCase));
			};
			var reported = new HashSet<int>();
			for (int i = 0; i < rules.Count; i++)
			{
				// (follow what the rule waits for: back to itself = a circle)
				var path = new List<int> { i };
				int j = dep(i);
				while (j >= 0 && !path.Contains(j) && path.Count <= rules.Count) { path.Add(j); j = dep(j); }
				if (j >= 0 && j == i && !reported.Contains(i))
				{
					foreach (int k in path) reported.Add(k);
					c.Add(i, Level.Problem, "Rules " + string.Join(" → ", path.Concat(new[] { i }).Select(k => "'" + (rules[k].Id.Length > 0 ? rules[k].Id : (k + 1).ToString()) + "'").ToArray()) + " wait for each other in a circle: none of them ever comes.",
						"Let one of them start another way (e.g. \"When the world starts\").");
				}
			}
			// A rule waiting for one that can't work, and "near" an island that may not be there yet
			var broken = new HashSet<int>(c.Out.Where(f => f.Level == Level.Problem && f.Rule >= 0).Select(f => f.Rule));
			for (int i = 0; i < rules.Count; i++)
			{
				int d = dep(i);
				if (d >= 0 && d != i && broken.Contains(d) && !broken.Contains(i))
					c.Add(i, Level.Warning, R(c, i) + " waits for rule " + (d + 1) + " '" + rules[d].Id + "', which has a problem (see above): while that isn't fixed, this rule never comes either.", "Fix rule " + (d + 1) + " first.");
				IntroRule r = rules[i];
				if (r.Where == "near" && r.WhereRef.Length > 0)
				{
					int n = rules.FindIndex(x => x.Id.Equals(r.WhereRef, StringComparison.OrdinalIgnoreCase));
					if (n >= 0 && n != i && !WaitsFor(dep, i, n) && !(rules[n].When == "start" && r.When != "start"))
						c.Add(i, Level.Tip, R(c, i) + " is placed near '" + r.WhereRef + "', but doesn't wait for it: if it fires first, it waits until '" + r.WhereRef + "' is in the world.", "Fine if that is what you want; else let its WHEN wait for '" + r.WhereRef + "'.");
				}
			}
		}

		/// <summary>Does rule i (through what it waits for) come after rule n?</summary>
		static bool WaitsFor(Func<int, int> dep, int i, int n)
		{
			int j = dep(i), guard = 0;
			while (j >= 0 && guard++ < 1000) { if (j == n) return true; j = dep(j); }
			return false;
		}

		static IEnumerable<string> StoryTips(WorldPlan plan, bool islandMode)
		{
			if (islandMode || !plan.ChangesStory) yield break;
			List<string> steps = StoryChain.BuildSteps(plan.RaftStory, plan.LeaveOut, plan.Rules);
			yield return "The story, in order: " + (steps.Count == 0 ? "no islands" : string.Join(" > ", steps.Select(s => StoryChain.IsRaft(s) ? StoryOrder.Name(StoryChain.TypeOfStep(s)) : "'" + StoryChain.RuleIdOf(s) + "'").ToArray()));
			if (steps.Count > 0 && steps[0] != StoryChain.RaftKey(ChunkPointType.Landmark_RadioTower)) yield return "Raft's story starts at the Radio Tower (recommended first)";
			if (steps.Count > 0 && steps[steps.Count - 1] != StoryChain.RaftKey(ChunkPointType.Landmark_Utopia)) yield return "Utopia is Raft's ending: without it last, the story has no ending";
			foreach (ChunkPointType t in StoryOrder.Chain.Where(t => !steps.Contains(StoryChain.RaftKey(t))))
			{
				List<string> needed = StoryChain.NeededBlueprintsOn(StoryOrder.Key(t));
				if (needed.Count > 0) yield return "Without " + StoryOrder.Name(t) + " there is no " + string.Join(", ", needed.ToArray()) + " blueprint: put them in a chest or a quest reward";
			}
		}

		static string List(IEnumerable<string> items)
		{
			var l = items.Select(x => "'" + x + "'").ToList();
			return l.Count <= 5 ? string.Join(", ", l.ToArray()) : string.Join(", ", l.Take(5).ToArray()) + " and " + (l.Count - 5) + " more";
		}

		#endregion
	}
}
