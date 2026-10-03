using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The library content's play tests: a saved island brought into a world and played as a player would, from a
	/// script (Mods\DynamicIslands\recipes\&lt;name&gt;.play; the sources are in the repository's content\tests). Positions
	/// are metres from the island's land middle, as in its recipe. Steps:
	///   island &lt;name&gt; [keep]           the island brought ahead of the raft (removed at the end unless kept)
	///   stand | at x z [y=|h=]          the player on its highest gentle spot / at a point (on what is there, y= above it,
	///                                   h= above the sea)
	///   walk x z to x z [to x z ...]    walked with Raft's own controller (no jumping, no flying): it must get there
	///   climb x z h=                    the player at a ladder's foot: Raft's controller takes hold of the ladder
	///   zone id | read title | open title | openat x z | use name | kill label | catch label [n]
	///   air id                          a diver in an air pocket (a shown zone with Air): breath back to full
	///   expect step n | story id n | shown name | hidden name | message text | item name n | animals label n | stand x z h=
	///   wait s | log text | hour h | picture file x y z lookx looky lookz (Raft's camera, its water - for the guide;
	///                                   heights above the sea, or "+h" above what is below)
	/// A world plan's test plays its story the same way (only in a test world 'CI ...'):
	///   plan &lt;name&gt; | plan end          the world gets the plan from its start / the world's story back to Raft's
	///   note &lt;Raft story island&gt;        Raft's note that gives that island's frequency read (the Receiver's own: RadioTower)
	///   tune &lt;rule&gt;                     the Receiver tuned to a plan island's frequency: it comes, and "island &lt;name&gt;"
	///                                   (an island's own test, included) plays the one the plan brought
	///   arrive &lt;rule&gt;                   the island a rule brings by itself (ahead, near another one) comes and is played
	///   sail km                         the raft has sailed that much further (a plan's km rules: its side trips)
	///   expect chain &lt;rule|Raft island&gt; done|unlocked|locked     its place in the story
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIPlay", docs: "Dev, world (host, a test world 'CI ...'): plays a library island from Mods\\DynamicIslands\\recipes\\<name>.play - the island brought into the world, walked, climbed, its quest done step by step with checks. CIPlay <name>")]
		public static void PlayCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Fail("CIPlay <name>"); return; }
			DynamicIslands.instance.StartCoroutine(PlayRoutine(string.Join(" ", args)));
		}

		static IslandWorldState.Entry playEntry;

		/// <summary>A world plan's test is playing ("plan" step): "island" plays the island the plan brought.</summary>
		static bool planMode;

		/// <summary>A point of the played island (metres from its land middle) in the world.</summary>
		static Vector3 PlayPoint(float x, float z) { return playEntry.Position + new Vector3(x - playOffset.x, 0f, z - playOffset.y); }

		/// <summary>How far the island's land centre lies from its recipe's origin ("offset" step; the recipe logs it when it
		/// saves): a world spawns an island by its land centre, the recipe's points are from its origin.</summary>
		static Vector2 playOffset;

		/// <summary>The top of what is at a point (land, a deck, a roof), below a height.</summary>
		static float PlaySurface(Vector3 p, float below = 400f)
		{
			RaycastHit hit;
			return Physics.Raycast(new Vector3(p.x, below, p.z), Vector3.down, out hit, below + 300f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : playEntry.Position.y;
		}

		static IEnumerator PlayRoutine(string name)
		{
			string path = Path.Combine(RecipeFolder, name.EndsWith(".play") ? name : name + ".play");
			if (!File.Exists(path)) { Fail("play: no file " + path); yield break; }
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost) { Fail("play " + name + ": host, in a world"); yield break; }
			List<RLine> lines;
			try { lines = ExpandRecipe(path); }
			catch (Exception e) { Fail("play " + name + ": " + e.Message); yield break; }
			yield return EnsureAlive();
			Network_Player me = RAPI.GetLocalPlayer();
			bool ok = true, keep = false;
			var made = new List<IslandWorldState.Entry>();
			playEntry = null;
			int checks = 0;
			foreach (RLine rl in lines)
			{
				string line = rl.Text.Trim();
				string[] t = Tokens(line);
				string verb = t[0].ToLowerInvariant();
				var opt = Options(t.Skip(1));
				bool storyStep = verb == "plan" || verb == "note" || verb == "tune" || verb == "arrive" || (verb == "expect" && t.Length > 1 && t[1] == "chain");
				if (verb != "island" && verb != "log" && verb != "wait" && verb != "hour" && !storyStep && playEntry == null) { Fail("play " + name + ", " + rl.Where + ": no island yet"); yield break; }
				// (the island step goes to a plan's island that isn't loaded yet - one a tuned frequency brought beyond the load distance)
				if (playEntry != null && playEntry.Root == null && verb != "log" && verb != "island" && !storyStep) { Fail("play " + name + ", " + rl.Where + ": the island isn't loaded"); yield break; }
				KeepAlive(me);
				switch (verb)
				{
					case "island":
					{
						string island = Rest(line, 1).Replace(" keep", "").Trim();
						keep = line.EndsWith(" keep");
						playOffset = Vector2.zero;
						// (a plan's test: the copy the plan brought - tuned to - is the one played)
						IslandWorldState.Entry planned = planMode ? IslandWorldState.Islands.FirstOrDefault(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(x.Rule)) : null;
						Vector3 offIsland = planned != null ? me.transform.position - planned.Position : Vector3.zero;
						offIsland.y = 0f;
						if (planned != null && (planned.Root == null || offIsland.magnitude > 300f))
						{
							// (it came where the plan brings it, ahead of the raft - out of reach of a player still on the last island:
							// it isn't loaded, or unloads a moment later. The player goes there, as a player sails there, and it loads)
							Vector3 toward = me.transform.position - planned.Position;
							toward.y = 0f;
							Vector3 near = planned.Position + (toward.sqrMagnitude > 1f ? toward.normalized : Vector3.back) * 150f;
							near.y = 0.5f;
							PlayerMove.To(me, near);
							for (float w = 0f; w < 40f && planned.Root == null; w += 0.5f) yield return new WaitForSeconds(0.5f);
							Log("  went to the plan's '" + island + "' (" + (planned.Root != null ? "loaded" : "still not loaded") + ")");
						}
						if (planned != null && planned.Root != null) playEntry = planned;
						else if (planned != null) { Fail("play " + name + ": the plan's '" + island + "' didn't load when the player came"); yield break; }
						else
						{
							// (a copy left by an earlier run that stopped half way: removed first)
							var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
							if (left.Count > 0) { IslandWorldState.RemoveIds(left, true); IslandCache.Forget(); Log("  (removed " + left.Count + " copy/copies of '" + island + "' left by an earlier run)"); yield return new WaitForSeconds(1f); }
							Vector3? spot = ScSpot(island, 400f);
							if (!spot.HasValue) { Fail("play " + name + ": no open sea for '" + island + "'"); yield break; }
							yield return ScBring(island, spot.Value, made);
							playEntry = made.LastOrDefault();
							if (playEntry == null || playEntry.Root == null) { Fail("play " + name + ": '" + island + "' didn't come"); yield break; }
						}
						yield return new WaitForSeconds(3f);
						// (a clean start: the crew holds none of the island's story items - an earlier run in this world left them)
						foreach (StoryItemDef d in StoryItems.Of(IslandCache.PropsOf(playEntry)))
							if (StoryBook.Count(d.Id) > 0) StoryBook.Take(d.Id, StoryBook.Count(d.Id));
						// (and empty hands in a test world: after many runs a full inventory took no more loot - 'expect item' failed)
						if ((SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ") && me.Inventory != null) me.Inventory.Clear();
						Log("  '" + island + "' is in the world at " + playEntry.Position.ToString("F0"));
						break;
					}
					case "stand":
						yield return StandRoutine(playEntry.Root);
						break;
					case "at":
					{
						Vector3 p = PlayPoint(F(t[1]), F(t[2]));
						// (on what is there: the player's middle a metre above it - put lower, the player started inside bare
						// ground and fell through it into the sea, and the island's pictures showed the water's wobble)
						p.y = opt.ContainsKey("h") ? playEntry.Position.y + F(opt["h"]) : PlaySurface(p) + 1.1f + (opt.ContainsKey("y") ? F(opt["y"]) : 0f);
						PlayerMove.To(me, p);
						yield return new WaitForSeconds(1f);
						break;
					}
					case "walk":
					{
						// walk x z to x z ...: from the first point, with Raft's controller at walking speed; stuck = failed
						// (h=: the walk ends about this high above the sea - on the deck, not fallen off it)
						var points = new List<Vector3>();
						var wopt = Options(t.Where(x => x.Contains("=")));
						string[] coords = t.Where(x => !x.Contains("=")).ToArray();
						bool blocked = coords.Contains("blocked");
						coords = coords.Where(x => x != "blocked").ToArray();
						for (int i = 1; i + 1 < coords.Length; i += 3) points.Add(PlayPoint(F(coords[i]), F(coords[i + 1])));
						Vector3 start = points[0];
						// (a little above what is there: the player lands on it, not half inside a thick plank)
						// (below=: what is there under that height - a deck under a crane's jib)
						start.y = PlaySurface(start, wopt.ContainsKey("below") ? playEntry.Position.y + F(wopt["below"]) : 400f) + 1.1f;
						PlayerMove.To(me, start);
						yield return new WaitForSeconds(1f);
						bool got = true;
						string where = "";
						yield return PlayWalk(me, points.Skip(1).ToList(), (g, w) => { got = g; where = w; });
						if (got && wopt.ContainsKey("h") && Mathf.Abs(me.transform.position.y - playEntry.Position.y - F(wopt["h"])) > 1.6f) { got = false; where += " - not at " + wopt["h"] + " m"; }
						// ("blocked": the way must be shut - a locked gate, a wall)
						if (blocked) Check(ref ok, !got, "the way " + line.Substring(5) + " is blocked: " + where);
						else Check(ref ok, got, "walked " + line.Substring(5) + ": " + where);
						checks++;
						break;
					}
					case "climb":
					{
						// the ladder nearest the point (its own climb collider): the player in it, a metre above its foot - Raft's
						// controller takes hold of the ladder
						Vector3 p = PlayPoint(F(t[1]), F(t[2]));
						Collider grip = playEntry.Root.GetComponentsInChildren<Collider>(true).Where(c => c.name.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0)
							.OrderBy(c => ScFlat(c.bounds.center, p)).FirstOrDefault();
						if (grip == null || ScFlat(grip.bounds.center, p) > 3f) { Check(ref ok, false, "a ladder near " + t[1] + "," + t[2] + (grip != null ? " (the nearest is " + ScFlat(grip.bounds.center, p).ToString("F1") + " m off: " + grip.transform.parent?.name + "/" + grip.name + " at " + (grip.bounds.center - playEntry.Position).ToString("F1") + ")" : "")); checks++; break; }
						float foot = opt.ContainsKey("h") ? playEntry.Position.y + F(opt["h"]) : grip.bounds.min.y;
						p = new Vector3(grip.bounds.center.x, Mathf.Max(foot, grip.bounds.min.y) + 1f, grip.bounds.center.z);
						PlayerMove.To(me, p);
						yield return new WaitForSeconds(0.3f);
						bool held = false;
						for (float s = 0f; s < 3f && !held; s += Time.deltaTime)
						{
							held = Climbing(me);
							yield return null;
						}
						Check(ref ok, held, "a ladder at " + F(t[1]) + "," + F(t[2]) + " takes hold of the player" + (held ? "" : " (Raft's controller isn't climbing)"));
						checks++;
						PlayerMove.To(me, p + Vector3.up * 0.1f);
						break;
					}
					case "zone":
						Check(ref ok, ScEnterZone(playEntry, Rest(line, 1).Trim()), "zone '" + Rest(line, 1).Trim() + "' there");
						yield return new WaitForSeconds(1.2f);
						break;
					case "air":
					{
						// air <zone id>: a diver in the air pocket breathes - put there with little breath left, it is full again a
						// moment later (only a shown zone: one still hidden isn't found)
						string zid = Rest(line, 1).Trim();
						TriggerZone pocket = playEntry.Root.GetComponentsInChildren<TriggerZone>(false).FirstOrDefault(x => x.Id == zid && x.Air);
						Check(ref ok, pocket != null, "an air pocket '" + zid + "' there");
						if (pocket == null) break;
						PlayerMove.To(me, pocket.transform.position);
						yield return new WaitForSeconds(0.5f);
						me.Stats.stat_oxygen.Value = me.Stats.stat_oxygen.Max * 0.1f;
						yield return new WaitForSeconds(1.2f);
						float breath = me.Stats.stat_oxygen.Value / Mathf.Max(0.01f, me.Stats.stat_oxygen.Max);
						Check(ref ok, breath > 0.9f, "breathing in the air pocket '" + zid + "': breath back to " + (breath * 100f).ToString("F0") + " %");
						break;
					}
					case "read":
					{
						// (a note, or the note in a chest - read once the chest is emptied, as its hint says)
						string title = Rest(line, 1).Trim();
						CustomNote inChest = playEntry.Root.GetComponentsInChildren<CustomNote>(true).FirstOrDefault(c => c.GetComponent<LootCrate>() != null && (c.Title ?? "").IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
						bool plain = playEntry.Root.GetComponentsInChildren<CustomNote>(true).Any(c => c.GetComponent<LootCrate>() == null && (c.Title ?? "").IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
						if (!plain && inChest != null) { PutPlayerNear(inChest.transform); NoteReader.Open(inChest); NoteReader.Close(); }
						else ScReadNote(playEntry, title);
						Check(ref ok, plain || inChest != null, "a note '" + title + "' to read");
						yield return new WaitForSeconds(1.2f);
						break;
					}
					case "open":
						Check(ref ok, ScOpenChest(playEntry, Rest(line, 1).Trim()) != null, "chest '" + Rest(line, 1).Trim() + "' opened");
						yield return new WaitForSeconds(1.2f);
						break;
					case "openat":
					{
						Vector3 p = PlayPoint(F(t[1]), F(t[2]));
						LootCrate chest = playEntry.Root.GetComponentsInChildren<LootCrate>(true).OrderBy(c => ScFlat(c.transform.position, p)).FirstOrDefault();
						Check(ref ok, chest != null && ScFlat(chest.transform.position, p) < 4f, "a chest at " + t[1] + "," + t[2] + (chest != null ? " (" + ScFlat(chest.transform.position, p).ToString("F1") + " m off)" : ""));
						if (chest != null) { PutPlayerNear(chest.transform); chest.LastGiven = new List<string>(); chest.Open(); }
						checks++;
						yield return new WaitForSeconds(1.2f);
						break;
					}
					case "use":
						Check(ref ok, ScObjOf(playEntry, Rest(line, 1).Trim()) != null, "object '" + Rest(line, 1).Trim() + "' there");
						ScUse(playEntry, Rest(line, 1).Trim());
						yield return new WaitForSeconds(1.2f);
						break;
					case "kill":
					{
						string label = Rest(line, 1).Trim();
						yield return ScWaitAnimals(playEntry, label, 1, 15f);
						List<AI_NetworkBehaviour> animals = ScAnimals(playEntry, label);
						Check(ref ok, animals.Count > 0, "animals '" + label + "' to defeat (" + animals.Count + ")");
						foreach (AI_NetworkBehaviour a in animals) { PutPlayerNear(a.transform); ScKill(a); yield return new WaitForSeconds(0.4f); }
						yield return new WaitForSeconds(2f);
						break;
					}
					case "catch":
					{
						// catch <label> [n]: animals caught as Raft's net does it (captured, carried off)
						string label = t.Length > 2 && !char.IsLetter(t[t.Length - 1][0]) ? string.Join(" ", t.Skip(1).Take(t.Length - 2).ToArray()) : Rest(line, 1).Trim();
						int want = t.Length > 2 && !char.IsLetter(t[t.Length - 1][0]) ? (int)F(t[t.Length - 1]) : 1;
						yield return ScWaitAnimals(playEntry, label, want, 15f);
						// (only the island's own: animals caught in an earlier run stay in the world, without a spawn spot)
						var animals = ScAnimals(playEntry, label).OfType<AI_NetworkBehaviour_Domestic>()
							.Where(an => an.connectedSpawner != null && an.connectedSpawner.transform.IsChildOf(playEntry.Root.transform)).Take(want).ToList();
						Check(ref ok, animals.Count >= want, "animals '" + label + "' to catch (" + animals.Count + " of " + want + ")");
						foreach (AI_NetworkBehaviour_Domestic a in animals) yield return ScCarryHome(a, false);
						yield return new WaitForSeconds(1f);
						break;
					}
					case "plan":
					{
						// plan <name>: the test world plays a world plan from its start (a clean slate of the story first);
						// plan end: the world's story back to Raft's, the plan's islands gone
						if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("play " + name + ": a plan only in a test world 'CI ...' (it changes the world's story)"); yield break; }
						string plan = Rest(line, 1).Trim();
						PlanCleanSlate();
						if (plan == "end")
						{
							WorldDirector.SetPlan(WorldPlan.RandomName, false);
							StoryChain.OnWorldRead();
							IslandWorldState.Save();
							planMode = false;
							playEntry = null;
							Log("  the world's story is Raft's again");
							break;
						}
						Check(ref ok, ScSetPlan(plan, true), "the world gets the plan '" + plan + "'");
						checks++;
						planMode = true;
						playEntry = null;
						yield return new WaitForSeconds(1f);
						Log("  the story: " + string.Join(" > ", StoryChain.Steps.Select(StoryChain.StepName).ToArray()));
						break;
					}
					case "note":
					{
						// note <Raft story island>: Raft's note that gives its frequency read, as reading it in the game does
						ChunkPointType nt = StoryOrder.Parse(Rest(line, 1).Trim());
						if (nt == ChunkPointType.None) { Check(ref ok, false, rl.Where + ": no Raft story island '" + Rest(line, 1).Trim() + "'"); break; }
						PlayNote(nt);
						StoryChain.Tick();
						yield return new WaitForSeconds(1f);
						break;
					}
					case "tune":
					{
						// tune <rule>: the Receiver tuned to a plan island's frequency (unlocked by then): the island comes
						string rule = Rest(line, 1).Trim();
						StoryChain.Tick();
						string freq = StoryChain.FrequencyOf(rule);
						Check(ref ok, freq != null, "a frequency on the Receiver for '" + rule + "' (" + (freq ?? "none") + ")");
						// (brought is enough: with the test's raft standing still, earlier islands take the spots ahead and one may come
						// beyond the load distance - Thornwood 1530 m off - loading as the player goes there, as in a game)
						yield return TuneTo(rule, 60f, false);
						IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Rule == rule);
						Check(ref ok, e != null, "tuned to " + freq + ": '" + rule + "' comes" + (e != null ? " ('" + e.HostName + "', " + (e.Position - me.transform.position).magnitude.ToString("F0") + " m away" + (e.Root == null ? ", loads as players come near" : "") + ")" : ""));
						checks += 2;
						if (e != null) { playEntry = e; playOffset = Vector2.zero; }
						break;
					}
					case "sail":
					{
						// sail <km>: the raft has sailed that much further - as the distance sailed counts it, for a plan's
						// km rules (The Long Voyage's side trips come at 2 to 32 km); "arrive <rule>" then waits for the island
						float km = t.Length > 1 ? F(t[1]) : 1f;
						WorldDirector.Sailed += km * 1000f;
						Log("  sailed " + Num(km) + " km more: " + (WorldDirector.Sailed / 1000f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " km in all");
						yield return new WaitForSeconds(0.5f);
						break;
					}
					case "arrive":
					{
						// arrive <rule>: the island a plan rule brings by itself (ahead of the raft at the start, near another island
						// when a quest is done, after a visit) comes; the steps after it play on it
						string rule = Rest(line, 1).Trim();
						StoryChain.Tick();
						yield return WaitFor(() => IslandWorldState.Islands.Any(x => x.Rule == rule && x.Root != null), 90f);
						IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Rule == rule && x.Root != null);
						Check(ref ok, e != null, "'" + rule + "' comes" + (e != null ? " ('" + e.HostName + "', " + (e.Position - me.transform.position).magnitude.ToString("F0") + " m away)" : ""));
						checks++;
						if (e != null) { playEntry = e; playOffset = Vector2.zero; }
						break;
					}
					case "expect":
						checks++;
						if (t.Length > 2 && t[1] == "chain")
						{
							// expect chain <rule|Raft story island> done|unlocked|locked: its place in the story (a moment allowed)
							ChunkPointType ct = StoryOrder.Parse(t[2]);
							string key = ct != ChunkPointType.None ? StoryChain.RaftKey(ct) : "rule:" + t[2], state = t.Length > 3 ? t[3].ToLowerInvariant() : "done";
							Func<bool> holds = () => state == "done" ? StoryChain.Done.Contains(key) : state == "locked" ? !StoryChain.Unlocked.Contains(key) && !StoryChain.Done.Contains(key) : StoryChain.Unlocked.Contains(key);
							for (float w = 0f; w < 15f && !holds(); w += 0.5f) { StoryChain.Tick(); yield return new WaitForSeconds(0.5f); }
							Check(ref ok, holds(), "the story: " + StoryChain.StepName(key) + " " + state + " (" + string.Join(" > ", StoryChain.Steps.Select(x => StoryChain.StepName(x) + (StoryChain.Done.Contains(x) ? " (done)" : StoryChain.Unlocked.Contains(x) ? " (unlocked)" : "")).ToArray()) + ")");
							break;
						}
						if (t.Length > 2 && t[1] == "spinsown")
						{
							// (turning around its own up axis: that axis stays put while the object turns - a water wheel)
							IslandObjectRef r = ScObjOf(playEntry, t[2]);
							if (r == null) { Check(ref ok, false, "'" + t[2] + "' to spin (not there)"); break; }
							Vector3 up0 = r.transform.up, fwd0 = r.transform.forward;
							yield return new WaitForSeconds(1f);
							Check(ref ok, Vector3.Angle(up0, r.transform.up) < 2f && Vector3.Angle(fwd0, r.transform.forward) > 5f, "'" + t[2] + "' turns around its own axis (its axis moved " + Vector3.Angle(up0, r.transform.up).ToString("F1") + " deg, it turned " + Vector3.Angle(fwd0, r.transform.forward).ToString("F1") + " deg)");
							break;
						}
						yield return new WaitForSeconds(0.3f);
						try { PlayExpect(t, line, ref ok); }
						catch (Exception e) { Check(ref ok, false, rl.Where + " (" + line + "): " + e.Message); }
						break;
					case "wait":
						yield return new WaitForSeconds(t.Length > 1 ? F(t[1]) : 1f);
						break;
					case "hour":
						AzureSkyHour(F(t[1]));
						yield return new WaitForSeconds(1f);
						break;
					case "offset":
						// offset <x> <z>: the island's land centre from its recipe's origin (the recipe says so when it saves)
						playOffset = new Vector2(F(t[1]), F(t[2]));
						break;
					case "where":
					{
						// where <name>: logs where the island's objects of that name are, in the test's island coordinates
						string what = Rest(line, 1).Trim();
						foreach (Transform tr in playEntry.Root.GetComponentsInChildren<Transform>(true).Where(x => x.parent != null && x.name.StartsWith(what, StringComparison.OrdinalIgnoreCase) && x.parent.name.IndexOf(what, StringComparison.OrdinalIgnoreCase) < 0))
						{
							Vector3 d = tr.position - playEntry.Position + new Vector3(playOffset.x, 0f, playOffset.y);
							Log("  where " + tr.name + ": " + d.x.ToString("F1", CultureInfo.InvariantCulture) + " " + d.z.ToString("F1", CultureInfo.InvariantCulture) + " h=" + d.y.ToString("F1", CultureInfo.InvariantCulture));
						}
						break;
					}
					case "weather":
						// weather <name>: Raft's weather changed at once (pictures in clear weather); weather alone lists them
						PlayWeather(t.Length > 1 ? Rest(line, 1).Trim() : "");
						yield return new WaitForSeconds(3f);
						break;
					case "log":
						Log("  " + Rest(line, 1));
						break;
					case "picture":
					{
						// (a height "+h": that far above what is below the camera - the sand, a roof, the reef)
						string file = t[1];
						Vector3 from = PlayPoint(F(t[2]), F(t[4]));
						from.y = t[3].StartsWith("+") ? PlaySurface(from) + F(t[3].Substring(1)) : playEntry.Position.y + F(t[3]);
						Vector3 look = PlayPoint(F(t[5]), F(t[7]));
						look.y = t[6].StartsWith("+") ? PlaySurface(look) + F(t[6].Substring(1)) : playEntry.Position.y + F(t[6]);
						yield return PlayPicture(file, from, look);
						break;
					}
					default:
						Check(ref ok, false, rl.Where + ": unknown step '" + verb + "'");
						break;
				}
			}
			if (!keep && made.Count > 0) { ScRemove(made); OnRaftCommand(); }
			if (planMode) { PlanCleanSlate(); WorldDirector.SetPlan(WorldPlan.RandomName, false); StoryChain.OnWorldRead(); IslandWorldState.Save(); planMode = false; OnRaftCommand(); }
			if (ok) Log("PASS: play " + name + " (" + checks + " checks)"); else Fail("play " + name);
		}

		/// <summary>A test world's story from nothing: none of Raft's story notes read, no plan island in the world, the story
		/// chain and the rules' state forgotten (as a new world's).</summary>
		static void PlanCleanSlate()
		{
			Dictionary<int, ChunkPointType> notes = StoryOrder.FrequencyNotes();
			NoteBook.unlockedNoteBookIndexes.RemoveAll(i => notes.ContainsKey(i));
			NoteBook.unlockedChunkPointType.RemoveAll(c => Chain.Contains(c) || (int)c >= StoryChain.ModTypeBase);
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(x => !string.IsNullOrEmpty(x.Rule)).Select(x => x.Id).ToList(), true);
			IslandCache.Forget();
			StoryChain.Reset();
			WorldDirector.Done.Clear();
			// (from the start: nothing sailed yet - a test world sailed 40 km in earlier tests brought every side trip at once)
			WorldDirector.Sailed = 0f;
		}

		static void PlayExpect(string[] t, string line, ref bool ok)
		{
			string what = t.Length > 1 ? t[1].ToLowerInvariant() : "";
			IslandQuest q = QuestTracker.QuestOf(playEntry);
			int step = QuestTracker.StepOf(playEntry);
			switch (what)
			{
				case "step":
					Check(ref ok, step == (int)F(t[2]), "the quest at step " + t[2] + " of " + q.Steps.Count + " (" + step + (step < q.Steps.Count ? ": " + q.Steps[step].Describe() : ": done") + ")");
					break;
				case "title":
					Check(ref ok, q.Title == Rest(line, 2).Trim(), "the quest's title '" + q.Title + "' (expected '" + Rest(line, 2).Trim() + "')");
					break;
				case "done":
					Check(ref ok, q.Exists && step >= q.Steps.Count, "the quest '" + q.Title + "' done (" + step + " of " + q.Steps.Count + ")");
					break;
				case "story":
					Check(ref ok, StoryBook.Count(t[2]) == (int)F(t[3]), "story item " + t[2] + ": " + StoryBook.Count(t[2]) + " (expected " + t[3] + ")");
					break;
				case "shown":
				case "hidden":
				{
					IslandObjectRef r = ScObjOf(playEntry, t[2]);
					bool shown = r != null && r.gameObject.activeInHierarchy;
					Check(ref ok, r != null && shown == (what == "shown"), "'" + t[2] + "' " + what + (r == null ? " (not there)" : ""));
					break;
				}
				case "message":
				{
					string want = Rest(line, 2).Trim();
					string[] got = new[] { Behaviours.LastMessage, IslandInfo.LastMessage, QuestTracker.LastMessage }.Concat(IslandInfo.Recent.Skip(Math.Max(0, IslandInfo.Recent.Count - 6))).ToArray();
					Check(ref ok, got.Any(m => (m ?? "").IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0), "a message with '" + want + "' (last: '" + string.Join("' / '", got.Take(3).Select(m => m ?? "").ToArray()) + "')");
					break;
				}
				case "item":
				{
					Network_Player p = RAPI.GetLocalPlayer();
					int have = p != null && p.Inventory != null ? p.Inventory.GetItemCount(t[2]) : 0;
					Check(ref ok, have >= (int)F(t[3]), "the player has " + t[2] + " x" + have + " (at least " + t[3] + ")");
					break;
				}
				case "animals":
				{
					// (expect animals <label, may have spaces> <count>)
					string label = string.Join(" ", t.Skip(2).Take(t.Length - 3).ToArray());
					int n = ScAnimals(playEntry, label).Count;
					Check(ref ok, n == (int)F(t[t.Length - 1]), "animals '" + label + "' alive: " + n + " (expected " + t[t.Length - 1] + ")");
					break;
				}
				case "stand":
				{
					// expect stand x z h=: what a player stands on there is about this high above the sea (a floor, a deck)
					// (below=: looked for under that height above the sea - a room's floor under its roof)
					Vector3 p = PlayPoint(F(t[2]), F(t[3]));
					var o = Options(t.Skip(4));
					float top = PlaySurface(p, o.ContainsKey("below") ? playEntry.Position.y + F(o["below"]) : 400f) - playEntry.Position.y, want = o.ContainsKey("h") ? F(o["h"]) : 0f;
					Check(ref ok, Mathf.Abs(top - want) < 0.6f, "standing at " + t[2] + "," + t[3] + ": " + top.ToString("F2") + " m above the sea (expected " + want + ")");
					break;
				}
				default:
					Check(ref ok, false, "unknown expectation '" + what + "'");
					break;
			}
		}

		/// <summary>Whether Raft's player controller is on a ladder (PersonController's own climbing state).</summary>
		static bool Climbing(Network_Player p)
		{
			PersonController pc = p != null ? p.PersonController : null;
			if (pc == null) return false;
			var f = typeof(PersonController).GetField("climbing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
			return f != null && (bool)f.GetValue(pc);
		}

		/// <summary>
		/// Walks the player through points with Raft's own CharacterController at walking speed (as BringIn does, without
		/// jumps): stairs and ramps the controller steps up, walls and gaps stop it. Each point must be reached (within
		/// 0.7 m) before it has been stuck for 2 s. An animal standing in the way is waited for, then moved aside (a player
		/// walks round it: Ranger's Rest's goats wander across its terrace); what stops it otherwise is named.
		/// </summary>
		static IEnumerator PlayWalk(Network_Player player, List<Vector3> points, Action<bool, string> result)
		{
			PersonController pc = player.PersonController;
			CharacterController cc = pc.controller;
			pc.SwitchControllerType(ControllerType.Ground);
			// (the points from the island: Raft shifts the whole world by 1000 m when the player wanders far from its
			// middle - a walk aimed at a point fixed in the world then went 1000 m off)
			foreach (Vector3 rel in points.Select(q => q - playEntry.Position).ToList())
			{
				float stuck = 0f, total = 0f;
				int animalWaits = 0, sidesteps = 0;
				Vector3 last = player.transform.position;
				Vector3 target = playEntry.Position + rel;
				while (ScFlat(player.transform.position, target = playEntry.Position + rel) > 0.7f)
				{
					KeepAlive(player);
					Vector3 d = target - player.transform.position;
					d.y = 0f;
					Vector3 dir = d.normalized;
					Look(player, Quaternion.LookRotation(dir).eulerAngles.y, 0f);
					cc.Move(dir * pc.normalSpeed * Time.deltaTime);
					Vector3 now = player.transform.position;
					stuck = ScFlat(now, last) < pc.normalSpeed * Time.deltaTime * 0.3f ? stuck + Time.deltaTime : 0f;
					last = now;
					total += Time.deltaTime;
					if (stuck > 2f || total > 40f)
					{
						Collider blocker = WhatBlocks(player, dir);
						AI_NetworkBehaviour animal = blocker != null ? blocker.GetComponentInParent<AI_NetworkBehaviour>() : null;
						// (nothing ahead, but an animal close by: a warthog charging from the side knocks the player about -
						// Thornwood's king and his guards round its stone ring, 2026-10-03)
						if (blocker == null) animal = NearestAnimal(now, 3f);
						if (animal != null && animalWaits < 2 && total < 40f)
						{
							animalWaits++;
							if (animalWaits == 2)
							{
								Vector3 aside = animal.transform.position + Vector3.Cross(Vector3.up, dir).normalized * 3f;
								UnityEngine.AI.NavMeshAgent agent = animal.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>();
								if (agent != null && agent.isOnNavMesh) agent.Warp(aside); else animal.transform.position = aside;
								Log("  " + animal.name + " stood in the way: moved aside");
							}
							else Log("  " + animal.name + " stands in the way: waiting for it");
							stuck = 0f;
							yield return new WaitForSeconds(3f);
							continue;
						}
						// (nothing a sphere at knee height finds ahead - a low lip of rock or ground: a player steps round it, one
						// way and then the other)
						if (blocker == null && sidesteps < 2 && total < 40f)
						{
							Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * (sidesteps == 0 ? 1f : -1f);
							sidesteps++;
							Log("  stuck with nothing ahead: a step to the side");
							for (float t = 0f; t < 0.6f; t += Time.deltaTime)
							{
								KeepAlive(player);
								cc.Move(side * pc.normalSpeed * Time.deltaTime);
								yield return null;
							}
							stuck = 0f;
							last = player.transform.position;
							continue;
						}
						result(false, "stuck at " + (now - playEntry.Position).ToString("F1") + " on the way to " + (target - playEntry.Position).ToString("F1") +
							" (in the way: " + (blocker != null ? BlockerName(blocker) : "nothing found ahead") + ")");
						yield break;
					}
					yield return null;
				}
			}
			result(true, "got there, " + (player.transform.position.y - playEntry.Position.y).ToString("F2") + " m above the sea");
		}

		/// <summary>What stops a walking player: the nearest collider within a metre ahead at waist height, not the player's own.</summary>
		/// <summary>The nearest living animal within this many metres of a point (or null).</summary>
		static AI_NetworkBehaviour NearestAnimal(Vector3 at, float within)
		{
			return UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().Where(a => a != null && a.isActiveAndEnabled && Vector3.Distance(a.transform.position, at) <= within)
				.OrderBy(a => Vector3.Distance(a.transform.position, at)).FirstOrDefault();
		}

		static Collider WhatBlocks(Network_Player player, Vector3 dir)
		{
			RaycastHit[] hits = Physics.SphereCastAll(player.transform.position + Vector3.up * 0.6f, 0.35f, dir, 1.2f, ~0, QueryTriggerInteraction.Ignore);
			return hits.Where(h => h.collider != null && !h.collider.transform.IsChildOf(player.transform)).OrderBy(h => h.distance).Select(h => h.collider).FirstOrDefault();
		}

		/// <summary>A collider's object as the island has it (its name, under which object of the island).</summary>
		static string BlockerName(Collider c)
		{
			if (c.GetComponent<Terrain>() != null) return "the ground (a step too high)";
			Transform t = c.transform, island = playEntry != null && playEntry.Root != null ? playEntry.Root.transform : null;
			while (t.parent != null && island != null && t.parent != island && t.parent.parent != island) t = t.parent;
			return t.name + (t != c.transform ? " (" + c.name + ")" : "") + " at " + (c.bounds.center - (playEntry != null ? playEntry.Position : Vector3.zero)).ToString("F1");
		}

		/// <summary>A picture with Raft's own camera (its water and light, no HUD, no held tool) for the guide:
		/// Mods\DynamicIslands\recipes\play_&lt;file&gt;.jpg 1280x720.</summary>
		/// <summary>
		/// Raft's weather set at once, by the name of one of its weathers, through Raft's weather manager (found by
		/// reflection) - the library's pictures were taken in a passing fog. Without a name (or none matching) it logs the
		/// weathers there are and how the manager sets one.
		/// </summary>
		static void PlayWeather(string name)
		{
			const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
			Type managerType = typeof(Network_Player).Assembly.GetType("WeatherManager");
			UnityEngine.Object manager = managerType != null ? UnityEngine.Object.FindObjectOfType(managerType) : null;
			if (manager == null) { Log("  weather: Raft's weather manager isn't there"); return; }
			Type weatherType = managerType.Assembly.GetType("Weather");
			List<UnityEngine.Object> weathers = weatherType != null ? Resources.FindObjectsOfTypeAll(weatherType).ToList() : new List<UnityEngine.Object>();
			// (Raft: SetWeather(Weather weather, bool instant))
			MethodInfo set = managerType.GetMethods(all).FirstOrDefault(m => m.Name == "SetWeather" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == weatherType);
			UnityEngine.Object pick = name.Length == 0 ? null : weathers.FirstOrDefault(w => w.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
			if (pick == null || set == null)
			{
				Log("  weathers: " + string.Join(", ", weathers.Select(w => w.name).Distinct().ToArray()) + " | set by: " + (set != null ? set.Name + "(" + string.Join(", ", set.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")" : "-"));
				foreach (MethodInfo m in managerType.GetMethods(all).Where(m => m.DeclaringType == managerType))
					Log("  manager: " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")");
				return;
			}
			object[] args = { pick, true };
			try { set.Invoke(set.IsStatic ? null : manager, args); Log("  weather: " + pick.name); }
			catch (Exception e) { Log("  weather: " + pick.name + " failed - " + (e.InnerException ?? e).Message); }
		}

		static IEnumerator PlayPicture(string file, Vector3 from, Vector3 look)
		{
			Camera cam = Camera.main;
			if (cam == null) yield break;
			yield return new WaitForEndOfFrame();
			int mask = cam.cullingMask;
			foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.layer != 0) cam.cullingMask &= ~(1 << r.gameObject.layer);
			Vector3 lp = cam.transform.localPosition; Quaternion lr = cam.transform.localRotation;
			byte[] pic;
			try
			{
				cam.transform.position = from;
				cam.transform.LookAt(look);
				pic = RenderCam(cam, 1280, 720).EncodeToJPG(88);
			}
			finally
			{
				cam.transform.localPosition = lp; cam.transform.localRotation = lr;
				cam.cullingMask = mask;
			}
			string path = Path.Combine(RecipeFolder, "play_" + file + ".jpg");
			File.WriteAllBytes(path, pic);
			Network_Player me = RAPI.GetLocalPlayer();
			PersonController pc = me != null ? me.PersonController : null;
			Log("PICTURE " + Path.GetFullPath(path) + (pc != null ? " (the player " + pc.controllerType + " at " + (me.transform.position - (playEntry != null ? playEntry.Position : Vector3.zero)).ToString("F1") + ")" : ""));
		}
	}
}
