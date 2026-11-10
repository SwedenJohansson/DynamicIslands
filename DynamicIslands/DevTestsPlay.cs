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
	///   reach title                     a chest the player can open from where the walk left them (Raft's use reach,
	///                                   2.5 m from the eye, nothing in between) - "open" puts the player next to it
	///   air id                          a diver in an air pocket (a shown zone with Air): breath back to full
	///   expect step n | story id n | shown name | hidden name | message text | item name n | animals label n | stand x z h= |
		///                                   height h (the player, above the sea); use name stay: used where the player stands
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
			StartTest(PlayRoutine(string.Join(" ", args)));
		}

		static IslandWorldState.Entry playEntry;
		static readonly Dictionary<string, List<Vector3>> animalsSeen = new Dictionary<string, List<Vector3>>();

		/// <summary>animalsmoved's measure (fix1107): each animal's own start (at the last expect animals of its label) and the
		/// furthest it got from it, sampled every 0.2 s - only its place at the check missed a rat that ran off and came back
		/// (the_abyss_rats: "moved 0.8 m", a rat wandering round its den).</summary>
		static readonly Dictionary<string, Dictionary<AI_NetworkBehaviour, Vector3>> animalsStart = new Dictionary<string, Dictionary<AI_NetworkBehaviour, Vector3>>();
		static readonly Dictionary<string, Dictionary<AI_NetworkBehaviour, float>> animalsFurthest = new Dictionary<string, Dictionary<AI_NetworkBehaviour, float>>();
		static readonly Dictionary<string, int> animalsSampleRun = new Dictionary<string, int>();

		static void AnimalsSample(string label)
		{
			Dictionary<AI_NetworkBehaviour, Vector3> st;
			Dictionary<AI_NetworkBehaviour, float> far;
			if (!animalsStart.TryGetValue(label, out st) || !animalsFurthest.TryGetValue(label, out far)) return;
			foreach (KeyValuePair<AI_NetworkBehaviour, Vector3> kv in st.ToList())
				if (kv.Key != null) far[kv.Key] = Mathf.Max(far[kv.Key], Vector3.Distance(kv.Value, kv.Key.transform.position));
		}

		static IEnumerator AnimalsSampler(string label, int run)
		{
			// (until the label is counted again; at most 10 minutes)
			for (float t = 0f; t < 600f && animalsSampleRun[label] == run; t += 0.2f)
			{
				AnimalsSample(label);
				yield return new WaitForSeconds(0.2f);
			}
		}

		/// <summary>How far an animal's feet are over the first thing under them that is not itself (-1: nothing within 3 m).</summary>
		static float AnimalFloorGap(AI_NetworkBehaviour a)
		{
			Vector3 from = a.transform.position + Vector3.up * 0.5f;
			float best = -1f;
			foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore))
				if (!h.collider.transform.IsChildOf(a.transform) && (best < 0 || h.distance < best)) best = h.distance;
			return best < 0 ? -1f : Mathf.Max(0f, best - 0.5f);
		}
		// Raft story blueprints the player held during this play (hands are emptied at each island: 'expect blueprints' counts these)
		static readonly HashSet<string> playBlueprints = new HashSet<string>();
		// (Hazards.Taken at the play's start or its last expect hurt / unhurt)
		static float playHurtFrom;
		static void NotePlayBlueprints(Network_Player me)
		{
			if (me == null || me.Inventory == null) return;
			ScrambledBlueprints.Read();
			foreach (string bp in ScrambledBlueprints.OnIslands.Keys) if (me.Inventory.GetItemCount(bp) > 0) playBlueprints.Add(bp);
		}

		/// <summary>A world plan's test is playing ("plan" step): "island" plays the island the plan brought.</summary>
		static bool planMode;

		/// <summary>A point of the played island (metres from its land middle) in the world.</summary>
		static Vector3 PlayPoint(float x, float z) { return playEntry.Position + new Vector3(x - playOffset.x, 0f, z - playOffset.y); }

		/// <summary>How far the island's land centre lies from its recipe's origin ("offset" step; the recipe logs it when it
		/// saves): a world spawns an island by its land centre, the recipe's points are from its origin.</summary>
		static Vector2 playOffset;

		/// <summary>The top of what is at a point (land, a deck, a roof), below a height.</summary>
		// A keypad high up (Riverfork's crane pad on its jib, 46 m over the ground): PutPlayerNear stands the player on the
		// terrain, past CodeLock's 8 m - stand them on what is under the pad instead, as a player who climbed there would.
		static void PutPlayerByHighPad(Transform pad)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || pad == null || Vector3.Distance(player.transform.position, pad.position) <= 6f) return;
			foreach (Vector3 side in new[] { new Vector3(0.9f, 0f, 0f), new Vector3(-0.9f, 0f, 0f), new Vector3(0f, 0f, 0.9f), new Vector3(0f, 0f, -0.9f) })
			{
				RaycastHit[] hits = Physics.RaycastAll(pad.position + side + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
				foreach (RaycastHit h in hits.OrderBy(x => x.distance))
				{
					if (h.collider.transform.IsChildOf(pad) || h.collider.transform.IsChildOf(player.transform)) continue;
					PlayerMove.To(player, h.point + Vector3.up * 1.2f);
					return;
				}
			}
		}

		static float PlaySurface(Vector3 p, float below = 400f, bool inCave = false)
		{
			if (inCave)
			{
				// (a cave's mountain over the spot with room under it: the walk starts in the cave, not on the hill over it - AU83,
				// Ironreef's Bear Hall)
				// (each surface down, one after another: RaycastAll gives each collider once - a cave piece's roof, not its floor
				// under it - and the walk started on the ground let down under that floor, X7: Ironreef's Bear Hall again)
				var hits = new List<RaycastHit>();
				Vector3 from = new Vector3(p.x, below, p.z);
				RaycastHit down;
				while (hits.Count < 40 && DownPastPlayers(from, from.y + 300f, out down))
				{
					hits.Add(down);
					from = down.point + Vector3.down * 0.02f;
				}
				if (hits.Count > 0 && hits[0].collider.name == CaveMountain.SkinName)
				{
					RaycastHit under = hits.Skip(1).FirstOrDefault(h => h.point.y < hits[0].point.y - 2.5f && h.collider.name != CaveMountain.SkinName);
					if (under.collider != null) return under.point.y;
				}
				// (a dug tunnel under the spot - 11-05: the first surface down with the passage over it, its floor - not the hill
				// over the tunnel; Ironreef's plays worked round it with below=)
				if (DigTunnel.AnyLive)
					foreach (RaycastHit dh in hits)
						if (DigTunnel.InPassage(dh.point + Vector3.up * 0.6f)) return dh.point.y;
			}
			RaycastHit hit;
			return DownPastPlayers(new Vector3(p.x, below, p.z), below + 300f, out hit) ? hit.point.y : playEntry.Position.y;
		}

		/// <summary>The first surface straight down, through any player (11-06: 'expect stand' at Shelter Atoll's blast gate
		/// read 3.1-3.2 m instead of the landing's 1.68 on and off - its ray met the top of the walker's own body when the
		/// walk had stopped right on the point, and the deck under it when a little to one side).</summary>
		static bool DownPastPlayers(Vector3 from, float dist, out RaycastHit hit)
		{
			for (int i = 0; i < 8; i++)
			{
				if (!Physics.Raycast(from, Vector3.down, out hit, dist, ~0, QueryTriggerInteraction.Ignore)) return false;
				if (hit.collider.GetComponentInParent<Network_Player>() == null) return true;
				float past = from.y - hit.point.y + 0.02f;
				from.y -= past;
				dist -= past;
			}
			hit = default(RaycastHit);
			return false;
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
			playBlueprints.Clear();
			playHurtFrom = Hazards.Taken;
			int checks = 0;
			// (settle time: a step's wait after its action is owed, not slept. The expects right after it are polled and
			// checked as soon as all of them hold - only when each was false before the action, so nothing true all along
			// passes early. Any other step, or an expect that can't pass early, waits the whole time as before; a step
			// that moves or looks (walk, at, picture...) after an early group still waits out the time it saved)
			float owed = 0f, settleLeft = 0f;
			List<RLine> settleGroup = null;
			bool settleAllFalse = false;
			int groupLeft = 0;
			for (int li = 0; li < lines.Count; li++)
			{
				hbWhat = "play " + name; hbStep = li; hbProg++;
				PutAwayBlueprint();
				RLine rl = lines[li];
				string line = rl.Text.Trim();
				string[] t = Tokens(line);
				string verb = t[0].ToLowerInvariant();
				var opt = Options(t.Skip(1));
				bool plainExpect = verb == "expect" && PlayPlainExpect(rl);
				bool okBefore = ok;
				if (!plainExpect) groupLeft = 0;
				if (!plainExpect && verb != "wait" && verb != "log" && owed > 0f) { yield return new WaitForSeconds(owed); owed = 0f; settleGroup = null; }
				// (an expect that reads where things are - height, stand - waits it out too: right after an early group of shown/hidden
				// it read a lift or hoist still on its way - Rookery hoist, 10-27)
				if (!plainExpect && verb != "log" && settleLeft > 0f) { if (!PlayNoSettleVerbs.Contains(verb) || (verb == "expect" && t.Length > 1 && PlayMotionExpects.Contains(t[1].ToLowerInvariant()))) yield return new WaitForSeconds(settleLeft); settleLeft = 0f; }
				if (PlayDeferVerbs.Contains(verb) && settleGroup == null && playEntry != null)
				{
					settleGroup = PlayExpectGroup(lines, li + 1);
					settleAllFalse = settleGroup.Count > 0 && settleGroup.All(g => !PlayQuiet(g));
				}
				// (sail only counts distance: a plan whose first rule wants km sails before any island - Relic of the Seventh Tide)
				bool storyStep = verb == "plan" || verb == "note" || verb == "tune" || verb == "arrive" || verb == "sail" || (verb == "expect" && t.Length > 1 && t[1] == "chain");
				if (verb != "island" && verb != "log" && verb != "wait" && verb != "hour" && !storyStep && playEntry == null) { Fail("play " + name + ", " + rl.Where + ": no island yet"); yield break; }
				// (the island step goes to a plan's island that isn't loaded yet - one a tuned frequency brought beyond the load distance)
				if (playEntry != null && playEntry.Root == null && verb != "log" && verb != "island" && !storyStep) { Fail("play " + name + ", " + rl.Where + ": the island isn't loaded"); yield break; }
				KeepAlive(me);
				switch (verb)
				{
					case "island":
					{
						// (PQ11: the island the play leaves, measured in the state the quest left it - CIReachOn)
						if (reachHook && playEntry != null && playEntry.Root != null) yield return ReachAnalyze(playEntry, playOffset, "after play " + name, n => { });
						string island = Rest(line, 1).Replace(" keep", "").Trim();
						keep = line.EndsWith(" keep");
						playOffset = Vector2.zero;
						// (a plan's test: the copy the plan brought - tuned to - is the one played; "island <rule>" names it by the
						// plan's rule - a generated island without a play of its own, The Abyss Expedition's sunken island)
						IslandWorldState.Entry planned = planMode ? IslandWorldState.Islands.FirstOrDefault(x => !string.IsNullOrEmpty(x.Rule) && (string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase) || x.Rule == island)) : null;
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
							for (float w = 0f; w < 40f && planned.Root == null; w += 0.1f) yield return new WaitForSeconds(0.1f);
							Log("  went to the plan's '" + island + "' (" + (planned.Root != null ? "loaded" : "still not loaded") + ")");
						}
						if (planned != null && planned.Root != null) playEntry = planned;
						else if (planned != null) { Fail("play " + name + ": the plan's '" + island + "' didn't load when the player came"); yield break; }
						else
						{
							// (a copy left by an earlier run that stopped half way: removed first)
							// (11-09 w03: a play started the moment a new world was ready brought its island before the director's
							// new-world start, which cleared the world's islands - "didn't come". It waits for that start first)
							for (float wt = 0f; wt < 60f && !(LoadSceneManager.IsGameSceneLoaded && WorldDirector.WorldHandled); wt += Time.unscaledDeltaTime) yield return null;
							var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
							if (left.Count > 0) { Log("  (removed " + left.Count + " copy/copies of '" + island + "' left by an earlier run)"); yield return RemoveIslandsAndWait(left); }
							Vector3? spot = ScSpot(island, 400f, IslandSpawner.ElevationOf(island));  // (a flying island flies here too)
							if (!spot.HasValue) { Fail("play " + name + ": no open sea for '" + island + "'"); yield break; }
							yield return ScBring(island, spot.Value, made);
							playEntry = made.LastOrDefault();
							if (playEntry == null || playEntry.Root == null) { Fail("play " + name + ": '" + island + "' didn't come"); yield break; }
						}
						yield return WaitIslandSettled(playEntry, 0.5f, 3f); // (until it is all there and still: 3 s at most, as before)
						// (a clean start: the crew holds none of the island's story items - an earlier run in this world left them)
						foreach (StoryItemDef d in StoryItems.Of(IslandCache.PropsOf(playEntry)))
							if (StoryBook.Count(d.Id) > 0) StoryBook.Take(d.Id, StoryBook.Count(d.Id));
						// (and empty hands in a test world: after many runs a full inventory took no more loot - 'expect item' failed)
						if ((SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ") && me.Inventory != null) { NotePlayBlueprints(me); me.Inventory.Clear(); }
						Log("  '" + island + "' is in the world at " + playEntry.Position.ToString("F0"));
						// (a flying island: seen from below, no sky between its underside and its ground - 10-16)
						if (playEntry.Position.y > IslandSpawner.FlyingThreshold)
						{
							string open = FlyingOpenFromBelow(playEntry.Root);
							Check(ref ok, open == null, "flying '" + island + "' seen from below: the underside meets the ground all round the coast" + (open != null ? " - " + open : ""));
						}
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
						start.y = PlaySurface(start, wopt.ContainsKey("below") ? playEntry.Position.y + F(wopt["below"]) : 400f, true) + 1.1f;
						PlayerMove.To(me, start);
						Vector3 islandAt = playEntry.Position;
						yield return new WaitForSeconds(1f);
						// (the points follow a world shift made in that second: they were 1000 m off - Frost Hollow, 11-07)
						Vector3 moved = playEntry.Position - islandAt;
						if (moved.sqrMagnitude > 1f) points = points.Select(q => q + moved).ToList();
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
					case "swim":
					{
						// swim x z h to x z h ...: under the sea, from the first point straight through the others with Raft's water
						// controller at swimming speed, up and down too; stuck = failed. A walk only goes along the floor - a flooded
						// cave's drop into a dug gallery and the climb back out (Lumen Shelf, The Stalker's Hollow, w03 11-09)
						string[] sc = t.Skip(1).Where(x => x != "to").ToArray();
						var spts = new List<Vector3>();
						for (int i = 0; i + 2 < sc.Length; i += 3) spts.Add(PlayPoint(F(sc[i]), F(sc[i + 1])) + Vector3.up * F(sc[i + 2]));
						if (spts.Count < 2) { Check(ref ok, false, "swim: two points or more (x z h to x z h)"); checks++; break; }
						PlayerMove.To(me, spts[0], ControllerType.Water);
						Vector3 sIsland = playEntry.Position;
						yield return new WaitForSeconds(1f);
						Vector3 sMoved = playEntry.Position - sIsland;
						if (sMoved.sqrMagnitude > 1f) spts = spts.Select(q => q + sMoved).ToList();
						bool sGot = true;
						string sWhere = "";
						yield return PlaySwim(me, spts.Skip(1).ToList(), (g, w) => { sGot = g; sWhere = w; });
						Check(ref ok, sGot, "swam " + line.Substring(5) + ": " + sWhere);
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
						owed += 1.2f;
						break;
					case "air":
					{
						// air <zone id> [here]: a diver in the air pocket breathes - put there with little breath left, it is full again a
						// moment later (only a shown zone: one still hidden isn't found). here: not put there - the player came in on
						// their own (a walk or swim before it) and must stand in the pocket already
						string[] aw = Rest(line, 1).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
						string zid = aw.Length > 0 ? aw[0] : "";
						bool airHere = aw.Length > 1 && aw[1] == "here";
						TriggerZone pocket = playEntry.Root.GetComponentsInChildren<TriggerZone>(false).FirstOrDefault(x => x.Id == zid && x.Air);
						Check(ref ok, pocket != null, "an air pocket '" + zid + "' there");
						if (pocket == null) break;
						if (airHere)
						{
							float off = Vector3.Distance(me.transform.position, pocket.transform.position);
							Check(ref ok, off <= pocket.Radius + 0.5f, "the player in the air pocket '" + zid + "' on their own (" + off.ToString("F1") + " m from its middle, radius " + pocket.Radius.ToString("F1") + ")");
						}
						else PlayerMove.To(me, pocket.transform.position);
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
						owed += 1.2f;
						break;
					}
					case "dig":
					{
						// (dig [n]: the island's buried treasure n (0 = the first) dug up as a player's shovel does - three digs -
						// and its chest picked up)
						int dn = t.Length > 1 ? int.Parse(t[1], CultureInfo.InvariantCulture) : 0;
						BuriedTreasure bt = playEntry.Root.GetComponentsInChildren<BuriedTreasure>(true).FirstOrDefault(b => b.Number == dn);
						Check(ref ok, bt != null && bt.Point != null, "buried treasure " + dn + " there");
						if (bt == null || bt.Point == null) break;
						MethodInfo digOnce = typeof(TreasurePoint).GetMethod("ProgressExcevation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
						PutPlayerNear(bt.transform, 1.5f);
						for (int di = 0; di < 3; di++) { digOnce.Invoke(bt.Point, new object[] { 1, true }); yield return new WaitForSeconds(0.6f); }
						yield return new WaitForSeconds(1.5f);
						PickupItem dug = bt.Point.pickupNetworked != null ? bt.Point.pickupNetworked.GetComponent<PickupItem>() : null;
						Check(ref ok, dug != null && !bt.Point.IsBuried, "treasure " + dn + " dug up");
						Pickup digger = RAPI.GetLocalPlayer().GetComponentInChildren<Pickup>(true);
						if (dug != null && digger != null) digger.PickupItemByType(dug, true);
						checks++;
						owed += 1.5f;
						break;
					}
					case "open":
						Check(ref ok, ScOpenChest(playEntry, Rest(line, 1).Trim()) != null, "chest '" + Rest(line, 1).Trim() + "' opened");
						owed += 1.2f;
						break;
					case "reach":
					{
						string why = ScReach(playEntry, Rest(line, 1).Trim());
						Check(ref ok, why == null, "chest '" + Rest(line, 1).Trim() + "' within reach of the player's eye" + (why != null ? " - " + why : ""));
						break;
					}
					case "openat":
					{
						Vector3 p = PlayPoint(F(t[1]), F(t[2]));
						LootCrate chest = playEntry.Root.GetComponentsInChildren<LootCrate>(true).OrderBy(c => ScFlat(c.transform.position, p)).FirstOrDefault();
						Check(ref ok, chest != null && ScFlat(chest.transform.position, p) < 4f, "a chest at " + t[1] + "," + t[2] + (chest != null ? " (" + ScFlat(chest.transform.position, p).ToString("F1") + " m off)" : ""));
						if (chest != null) { PutPlayerNear(chest.transform); chest.LastGiven = new List<string>(); chest.Open(); }
						checks++;
						owed += 1.2f;
						break;
					}
					case "use":
					{
						// (use <name> stay: used from where the player is - standing on a lift, it carries them)
						string what = Rest(line, 1).Trim();
						// (use <islander> pick <n>, 11-05: answer n of the islander's dialogue picked as a player's click does - ends in
						// Islanders.Talked with that choice, so its on.choice effects are checked in this play; a CITalk after the play
						// can't be, as the next play spawns the island again)
						System.Text.RegularExpressions.Match pm = System.Text.RegularExpressions.Regex.Match(what, @"^(.+?)\s+pick\s+(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
						if (pm.Success)
						{
							string who = pm.Groups[1].Value.Trim();
							int answer = int.Parse(pm.Groups[2].Value);
							IslandObjectRef talker = ScObjOf(playEntry, who);
							Islander isl = talker != null ? talker.GetComponentInChildren<Islander>(true) : null;
							Check(ref ok, isl != null, "islander '" + who + "' there");
							if (isl == null) break;
							PutPlayerNear(talker.transform);
							int talks = Islanders.Talks;
							DialoguePanel.LastClose = null;
							isl.Talk();
							Check(ref ok, DialoguePanel.IsOpen, "'" + who + "' opens a dialogue");
							yield return new WaitForSeconds(0.4f);
							Check(ref ok, DialoguePanel.IsOpen, "'" + who + "''s dialogue stays open (" + (DialoguePanel.LastClose ?? "-") + ")");
							DialoguePanel.Pick(answer);
							Check(ref ok, !DialoguePanel.IsOpen && Islanders.Talks > talks, "answer " + answer + " picked: dialogue closed, Islanders.Talked ran");
							Log("  (islander '" + who + "': answer " + answer + " picked)");
							owed += 1.2f;
							break;
						}
						bool stay = what.EndsWith(" stay");
						if (stay) what = what.Substring(0, what.Length - 5).Trim();
						IslandObjectRef used = ScObjOf(playEntry, what);
						Check(ref ok, used != null, "object '" + what + "' there");
						// (standing on a lift: its ride takes the full time, as before)
						if (stay) { if (used != null) Behaviours.Fire(playEntry, used.Index, "use", true); yield return new WaitForSeconds(1.2f); }
						else { ScUse(playEntry, what); owed += 1.2f; }
						break;
					}
					case "code":
					{
						// (code <name> <digits> [wrong]: the object's keypad code lock opened as a player does it - CodeLock;
						// "wrong": the digits must not open it)
						bool wrong = t.Length > 3 && t[t.Length - 1] == "wrong";
						int dn = wrong ? t.Length - 2 : t.Length - 1;
						string digits = t[dn];
						string what = string.Join(" ", t.Skip(1).Take(dn - 1).ToArray());
						IslandObjectRef pad = ScObjOf(playEntry, what);
						Check(ref ok, pad != null && CodeLock.HasCode(pad.Props), "object '" + what + "' with a keypad code");
						if (pad == null || !CodeLock.HasCode(pad.Props)) break;
						PutPlayerNear(pad.transform);
						PutPlayerByHighPad(pad.transform);
						CodeLock.Use(playEntry, pad.Index, ObjectProps.Get(pad.Props, CodeLock.Code), pad.transform);
						yield return new WaitForSeconds(0.6f);
						foreach (char c in digits) CodeLock.Press(c.ToString());
						CodeLock.Press("OK");
						yield return new WaitForSeconds(1.2f);
						if (wrong) Check(ref ok, !CodeLock.Unlocked(playEntry, pad.Index), "code " + digits + " doesn't open '" + what + "'");
						else Check(ref ok, !CodeLock.IsOpen && CodeLock.Unlocked(playEntry, pad.Index), "code " + digits + " opens '" + what + "'");
						CodeLock.Close();
						break;
					}
					case "kill":
					{
						string label = Rest(line, 1).Trim();
						yield return ScWaitAnimals(playEntry, label, 1, 15f);
						List<AI_NetworkBehaviour> animals = ScAnimals(playEntry, label);
						Check(ref ok, animals.Count > 0, "animals '" + label + "' to defeat (" + animals.Count + ")");
						// (then back where the player stood: left by the last animal, a later 'reach' step was done before the play
						// walked there)
						Network_Player killer = RAPI.GetLocalPlayer();
						Vector3 back = killer != null ? killer.transform.position - playEntry.Position : Vector3.zero;
						ControllerType backType = killer != null ? killer.PersonController.controllerType : ControllerType.Ground;
						foreach (AI_NetworkBehaviour a in animals) { if (a == null) continue; PutPlayerNear(a.transform); ScKill(a); yield return new WaitForSeconds(0.4f); } // (a puffer fish that burst is gone: Rootcellar Deep 10-25)
						if (killer != null && animals.Count > 0) { PlayerMove.To(killer, playEntry.Position + back, backType); KeepAlive(killer); }
						owed += 2f;
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
						owed += 1f;
						break;
					}
					case "plan":
					{
						// plan <name>: the test world plays a world plan from its start (a clean slate of the story first);
						// plan end: the world's story back to Raft's, the plan's islands gone
						if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("play " + name + ": a plan only in a test world 'CI ...' (it changes the world's story)"); yield break; }
						string plan = Rest(line, 1).Trim();
						// (11-09 w03: a play started the moment a new world was ready set its plan before the director's new-world
						// start, which set the world's plan back to 'Random islands' - the story stayed Raft's. It waits for that first)
						for (float wt = 0f; wt < 60f && !(LoadSceneManager.IsGameSceneLoaded && WorldDirector.WorldHandled); wt += Time.unscaledDeltaTime) yield return null;
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
						yield return TuneTo(rule, 60f, false, () => StoryChain.WaitsForRoom(rule));
						// (no room ahead of the test's raft, which stood still while the player went from island to island: the earlier
						// islands lie all round it. In a game the raft is at the last island. The raft moved 600 m on, up to 4 times -
						// The End of the Ocean's Command Well (300 m) and The City That Never Sank's Foundation Piles (400 m), 11-07)
						for (int move = 0; move < 4 && !IslandWorldState.Islands.Any(x => x.Rule == rule) && StoryChain.WaitsForRoom(rule); move++)
						{
							Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
							if (raft == null || raft.body == null) break;
							Vector3 dir = Flat(Raft.direction).sqrMagnitude > 0.01f ? Flat(Raft.direction).normalized : Vector3.forward;
							raft.body.position = raft.body.position + dir * 600f;
							raft.body.velocity = Vector3.zero;
							Physics.SyncTransforms();
							OnRaftCommand();
							Log("  (no room for '" + rule + "' ahead of the raft, which stood still: the raft moved 600 m on)");
							yield return new WaitForSeconds(3f);
							StoryChain.RetryNow(rule);
							yield return TuneTo(rule, 40f, false);
						}
						// (one that comes within the load distance spawns a moment later: the steps after it need it loaded - The
						// Abyss Expedition's sunken island, 293 m off, wasn't yet)
						yield return WaitFor(() => IslandWorldState.Islands.Any(x => x.Rule == rule && (x.Root != null || (x.Position - me.transform.position).magnitude > 450f)), 30f);
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
						// (brought is enough: one brought past the unload distance from a player still on the last island loads,
						// unloads a moment later and loads again as the raft comes near - "island" takes the player there. Waiting for
						// it loaded failed a plan run now and then: Silver Screen's Keeper's Light brought 900 m off, 2026-10-19)
						Func<bool> came = () => IslandWorldState.Islands.Any(x => x.Rule == rule && !x.Failed && (x.Root != null || !x.Loading));
						yield return WaitFor(came, 20f);
						// (not yet: most often no free spot ahead of the test's raft, which stood still while the earlier islands came
						// all round it - in a game it sails on. The raft moves 600 m on, up to 4 times: The Long Voyage's Crane Yard
						// waited with Thornwood 148 m off while the player swam at Mayor's Wharf, and a puffer fish killed him, 11-07)
						for (int move = 0; move < 4 && !came(); move++)
						{
							Raft raft = UnityEngine.Object.FindObjectOfType<Raft>();
							if (raft == null || raft.body == null) break;
							Vector3 dir = Flat(Raft.direction).sqrMagnitude > 0.01f ? Flat(Raft.direction).normalized : Vector3.forward;
							raft.body.position = raft.body.position + dir * 600f;
							raft.body.velocity = Vector3.zero;
							Physics.SyncTransforms();
							OnRaftCommand();
							Log("  ('" + rule + "' not here after a while: the raft, which stood still, moved 600 m on)");
							yield return WaitFor(came, 20f);
						}
						IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Rule == rule && !x.Failed);
						Check(ref ok, e != null, "'" + rule + "' comes" + (e != null ? " ('" + e.HostName + "', " + (e.Position - me.transform.position).magnitude.ToString("F0") + " m away" + (e.Root == null ? ", not loaded yet" : "") + ")" : ""));
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
							// (up to 3 s: a camera that sweeps to and fro may be at its turning point in the first second - SL6, 2026-10-19)
							float tilt = 0f, turned = 0f;
							for (int k = 0; k < 3 && turned <= 5f; k++)
							{
								yield return new WaitForSeconds(1f);
								tilt = Mathf.Max(tilt, Vector3.Angle(up0, r.transform.up)); turned = Mathf.Max(turned, Vector3.Angle(fwd0, r.transform.forward));
							}
							Check(ref ok, tilt < 2f && turned > 5f, "'" + t[2] + "' turns around its own axis (its axis moved " + tilt.ToString("F1") + " deg, it turned " + turned.ToString("F1") + " deg)");
							break;
						}
						if (groupLeft > 0) groupLeft--;  // (its group held together: checked at once)
						else if (owed > 0f && settleAllFalse && settleGroup != null && settleGroup.Count > 0 && settleGroup[0] == rl)
						{
							// (polled for at most the time it waited before: owed + its 0.3 s)
							float budget = owed + 0.3f, w = 0f;
							bool all = settleGroup.All(PlayQuiet);
							while (!all && w < budget) { yield return new WaitForSeconds(0.1f); w += 0.1f; all = settleGroup.All(PlayQuiet); }
							settleLeft = Mathf.Max(0f, budget - w);
							groupLeft = all ? settleGroup.Count - 1 : 0;
							owed = 0f; settleGroup = null;
						}
						else { yield return new WaitForSeconds(owed + 0.3f); owed = 0f; settleGroup = null; }
						try { PlayExpect(t, line, ref ok); }
						catch (Exception e) { Check(ref ok, false, rl.Where + " (" + line + "): " + e.Message); }
						break;
					case "wait":
						owed += t.Length > 1 ? F(t[1]) : 1f;
						break;
					case "hour":
						AzureSkyHour(F(t[1]));
						yield return new WaitForSeconds(1f);
						break;
					case "offset":
						// offset <x> <z>: the island's land centre from its recipe's origin (the recipe says so when it saves)
						playOffset = new Vector2(F(t[1]), F(t[2]));
						break;
					case "storespot":
						// storespot [x z] [n] [size]: flat clear ground for a store, nearest to x z (SL7)
						StoreSpots(playEntry, playOffset, t.Length > 2 ? new Vector2(F(t[1]), F(t[2])) : Vector2.zero, t.Length > 3 ? (int)F(t[3]) : 6, t.Length > 4 ? F(t[4]) : 8f);
						break;
					case "where":
					{
						// where <name>: logs where the island's objects of that name are, in the test's island coordinates
						string what = Rest(line, 1).Trim();
						foreach (Transform tr in playEntry.Root.GetComponentsInChildren<Transform>(true).Where(x => x.parent != null && x.name.StartsWith(what, StringComparison.OrdinalIgnoreCase) && x.parent.name.IndexOf(what, StringComparison.OrdinalIgnoreCase) < 0))
						{
							Vector3 d = tr.position - playEntry.Position + new Vector3(playOffset.x, 0f, playOffset.y);
							// (and its solid colliders' extent x/z, in the same coordinates: what a walk meets - 11-07)
							Vector3 sh = new Vector3(playOffset.x, 0f, playOffset.y) - playEntry.Position;
							string solid = string.Join(" ", tr.GetComponentsInChildren<Collider>(false).Where(c => c.enabled && !c.isTrigger).Select(c =>
								"x " + (c.bounds.min.x + sh.x).ToString("F2", CultureInfo.InvariantCulture) + ".." + (c.bounds.max.x + sh.x).ToString("F2", CultureInfo.InvariantCulture)
								+ " z " + (c.bounds.min.z + sh.z).ToString("F2", CultureInfo.InvariantCulture) + ".." + (c.bounds.max.z + sh.z).ToString("F2", CultureInfo.InvariantCulture)).ToArray());
							Log("  where " + tr.name + ": " + d.x.ToString("F1", CultureInfo.InvariantCulture) + " " + d.z.ToString("F1", CultureInfo.InvariantCulture) + " h=" + d.y.ToString("F1", CultureInfo.InvariantCulture) + (solid.Length > 0 ? " solid " + solid : ""));
						}
						break;
					}
					case "surface":
					{
						// surface <x0> <z0> <x1> <z1> [step]: logs the top surface's height (island frame, as stand) on a grid - where
						// a cave hill's dome really lies before a pit or a dig goes near it (w03 11-09: the bone pit was buried)
						float sx0 = F(t[1]), sz0 = F(t[2]), sx1 = F(t[3]), sz1 = F(t[4]), sst = t.Length > 5 ? F(t[5]) : 4f;
						for (float sz = sz1; sz >= sz0 - 0.01f; sz -= sst)
						{
							var row = new System.Text.StringBuilder("  surface z=" + sz.ToString("F1", CultureInfo.InvariantCulture) + ":");
							for (float sx = sx0; sx <= sx1 + 0.01f; sx += sst)
								row.Append(" " + (PlaySurface(PlayPoint(sx, sz), 400f) - playEntry.Position.y).ToString("F1", CultureInfo.InvariantCulture));
							Log(row.ToString());
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
					case "hide":
					case "show":
					{
						// hide <name> / show <name>: the island's renderers on objects of that exact name ("<name>/": and all under
						// them) turned off or on - which piece draws an odd look in a picture (the den wall's blades, 11-07)
						string what = Rest(line, 1).Trim();
						bool under = what.EndsWith("/");
						what = what.TrimEnd('/');
						int count = 0;
						foreach (Renderer r in playEntry.Root.GetComponentsInChildren<Renderer>(true))
						{
							bool hit = false;
							for (Transform tr = r.transform; tr != null && tr != playEntry.Root.transform && !hit; tr = under ? tr.parent : null)
								hit = string.Equals(tr.name, what, StringComparison.OrdinalIgnoreCase);
							if (!hit) continue;
							r.enabled = verb == "show";
							count++;
						}
						Log("  " + verb + " " + what + ": " + count + " renderer(s)");
						break;
					}
					case "empty":
						// the player's inventory emptied, as a player makes room (a long play carries a full bag by a vault whose plans
						// would drop to the floor - Sundown's bank, SL6 2026-10-19); only in a CI world, its blueprints noted first
						if (IsTestWorld() && me != null && me.Inventory != null) { NotePlayBlueprints(me); me.Inventory.Clear(); Log("  inventory emptied"); }
						break;
					case "picture":
					{
						// (a height "+h": that far above what is below the camera - the sand, a roof, the reef)
						string file = t[1];
						Vector3 from = PlayPoint(F(t[2]), F(t[4]));
						from.y = t[3].StartsWith("+") ? PlaySurface(from) + F(t[3].Substring(1)) : playEntry.Position.y + F(t[3]);
						Vector3 look = PlayPoint(F(t[5]), F(t[7]));
						look.y = t[6].StartsWith("+") ? PlaySurface(look) + F(t[6].Substring(1)) : playEntry.Position.y + F(t[6]);
						// (aim=<animals label>: turned to the living animals of that label - rats move, and a picture at their spawn
						// showed an empty drift; light[=m]: a spot light from the camera along the look, as a player's flashlight -
						// a dug tunnel is dark. Old Mine round 6, 11-06)
						string aimL;
						if (opt.TryGetValue("aim", out aimL))
						{
							List<AI_NetworkBehaviour> an = ScAnimals(playEntry, aimL).Where(a => Vector3.Distance(a.transform.position, from) < 30f).ToList();
							if (an.Count > 0) { Vector3 m = Vector3.zero; foreach (AI_NetworkBehaviour a in an) m += a.transform.position; look = m / an.Count + Vector3.up * 0.25f; }
							Log("  picture " + file + ": " + an.Count + " living '" + aimL + "' within 30 m" + (an.Count > 0 ? ", aimed at their middle" : ", aimed as written"));
						}
						float lightM = opt.ContainsKey("light") ? (opt["light"] == "" ? 25f : F(opt["light"])) : 0f;
						yield return PlayPicture(file, from, look, lightM);
						break;
					}
					case "beam":
					{
						// (beam <emitter>: its beam traced now and logged - where it starts, by which mirrors, what it ends on; the
						// emitter's renderers once each, as they put the beam's start at their middle)
						if (t.Length < 2) { Check(ref ok, false, rl.Where + ": beam <emitter>"); break; }
						IslandObjectRef o = ScObjOf(playEntry, t[1]);
						LaserBeam lb = o != null ? o.GetComponent<LaserBeam>() : null;
						Check(ref ok, lb != null, "laser emitter '" + t[1] + "' there");
						if (lb == null) break;
						Physics.SyncTransforms();
						lb.Trace();
						Vector3 e0 = playEntry.Position;
						Log("  beam '" + t[1] + "': at " + (o.transform.position - e0).ToString("F2") + ", forward " + o.transform.forward.ToString("F2") + ", starts " + (lb.Origin - e0).ToString("F2")
							+ "; " + lb.Points.Count + " points " + string.Join(" -> ", lb.Points.Select(p => (p - e0).ToString("F1")).ToArray()) + ", mirrors " + lb.Mirrors.Count
							+ ", ends on " + (lb.Hit != null ? lb.Hit.Name : lb.HitCollider != null ? "'" + lb.HitCollider.name + "' (" + (lb.HitCollider.transform.root != null ? lb.HitCollider.transform.root.name : "-") + ")" : "nothing"));
						// (the island's mirrors: where they stand and face, active, their colliders - a beam that passes one)
						foreach (IslandObjectRef m in playEntry.Root.GetComponentsInChildren<IslandObjectRef>(true).Where(LaserBeam.IsMirror))
							Log("    mirror '" + m.Name + "' at " + (m.transform.position - e0).ToString("F2") + ", forward " + m.transform.forward.ToString("F2") + (m.gameObject.activeInHierarchy ? "" : " (inactive)") + ", colliders: " +
								string.Join(", ", m.GetComponentsInChildren<Collider>(true).Select(c => c.name + (c.enabled ? "" : " off") + (c.isTrigger ? " trigger" : "") + " " + (c.bounds.center - e0).ToString("F1") + " size " + c.bounds.size.ToString("F1")).ToArray()));
						foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
							Log("    " + r.GetType().Name + " '" + r.name + "' " + (r.enabled ? "on" : "off") + (r.gameObject.activeInHierarchy ? "" : " (inactive)") + ", middle " + (r.bounds.center - e0).ToString("F2") + ", size " + r.bounds.size.ToString("F2"));
						break;
					}
					case "snap":
					{
						// (snap <file> <object> [from] [dist] [up]: the object's middle seen from compass bearing "from" (0 = from the north), dist m off, up m higher;
						// dist defaults to twice its size, at least 4 m)
						if (t.Length < 3) { Check(ref ok, false, rl.Where + ": snap <file> <object> [from] [dist] [up]"); break; }
						// (the object's name may have spaces - an islander's obj.name, "Harbourmistress Elaine", w03 11-09: the numbers
						// at the end are from/dist/up, the words before them the name)
						{
							int nEnd = t.Length; float tmpF;
							while (nEnd > 3 && t.Length - nEnd < 3 && float.TryParse(t[nEnd - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tmpF)) nEnd--;
							if (nEnd > 3) t = new[] { t[0], t[1], string.Join(" ", t, 2, nEnd - 2) }.Concat(t.Skip(nEnd)).ToArray();
						}
						IslandObjectRef o = ScObjOf(playEntry, t[2]);
						if (o == null)
						{
							// (not under the island's root: anywhere in the world, said so)
							o = Resources.FindObjectsOfTypeAll<IslandObjectRef>().FirstOrDefault(x => x.gameObject.scene.IsValid() && string.Equals(x.Name, t[2], StringComparison.OrdinalIgnoreCase));
							if (o != null) Log("  (snap: '" + t[2] + "' is not under the island's root but under '" + (o.transform.root != null ? o.transform.root.name : "-") + "', active " + o.gameObject.activeInHierarchy + ")");
						}
						Check(ref ok, o != null, "object '" + t[2] + "' there");
						if (o == null) break;
						Renderer[] rs = o.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
						Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(o.transform.position, Vector3.one);
						foreach (Renderer r in rs) b.Encapsulate(r.bounds);
						float bearing = t.Length > 3 ? F(t[3]) : 180f;
						float dist = t.Length > 4 ? F(t[4]) : Mathf.Max(4f, 2f * b.size.magnitude);
						float up = t.Length > 5 ? F(t[5]) : dist * 0.4f;
						Vector3 dir = new Vector3(Mathf.Sin(bearing * Mathf.Deg2Rad), 0f, Mathf.Cos(bearing * Mathf.Deg2Rad));
						Log("  snap '" + t[2] + "' at " + (o.transform.position - playEntry.Position).ToString("F1") + ", size " + b.size.ToString("F1"));
						yield return PlayPicture(t[1], b.center + dir * dist + Vector3.up * up, b.center);
						break;
					}
					default:
						Check(ref ok, false, rl.Where + ": unknown step '" + verb + "'");
						break;
				}
				// (fail fast: a quest or story step that didn't come leaves every later step waiting out its time for what
				// follows from it - a plan's next tune/arrive 90 s, an island 40 s. The play ends at that FAIL)
				string kind = t.Length > 1 ? t[1].ToLowerInvariant() : "";
				if (okBefore && !ok && (verb == "tune" || verb == "arrive" || (verb == "expect" && (kind == "step" || kind == "done" || kind == "chain"))))
				{
					Log("  (play ended at this FAIL, " + rl.Where + ": the " + (lines.Count - li - 1) + " steps after it follow from it)");
					break;
				}
			}
			if (owed > 0f) yield return new WaitForSeconds(owed);
			if (reachHook && playEntry != null && playEntry.Root != null) yield return ReachAnalyze(playEntry, playOffset, "after play " + name, n => { });
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

		// (steps whose wait after the action is settle time, owed to the expects after them)
		static readonly HashSet<string> PlayDeferVerbs = new HashSet<string> { "zone", "read", "open", "openat", "use", "kill", "catch", "dig", "wait" };
		// (steps that may follow an early group at once: they act on what the expects saw, they don't move or look)
		static readonly HashSet<string> PlayNoSettleVerbs = new HashSet<string> { "zone", "read", "open", "openat", "use", "code", "dig", "reach", "expect", "note" };
		// (expects that read where things are: they wait out a step's whole settle time, also after an early group)
		// (hurt/unhurt measure what the time did: after an early "expect message" group they were checked at once and saw one
		// hit of a 16-20 s wait - Blightmire and Engine Court hurt plays, 11-05)
		static readonly HashSet<string> PlayMotionExpects = new HashSet<string> { "height", "stand", "hurt", "unhurt", "animalsmoved" };
		// (expects that only read the state - no wait of their own, no side effect)
		static readonly HashSet<string> PlayQuietKinds = new HashSet<string> { "step", "title", "done", "story", "survivor", "shown", "hidden", "message", "item", "animals", "animalsinside" };

		static bool PlayPlainExpect(RLine r)
		{
			string[] t = Tokens(r.Text.Trim());
			return t.Length > 1 && t[0].ToLowerInvariant() == "expect" && PlayQuietKinds.Contains(t[1].ToLowerInvariant());
		}

		/// <summary>The plain expects right after a step (past any 'wait' lines): the ones its settle time is for.</summary>
		static List<RLine> PlayExpectGroup(List<RLine> lines, int from)
		{
			var g = new List<RLine>();
			int i = from;
			while (i < lines.Count && Tokens(lines[i].Text.Trim()).FirstOrDefault() == "wait") i++;
			while (i < lines.Count && PlayPlainExpect(lines[i])) g.Add(lines[i++]);
			return g;
		}

		/// <summary>An expect's result without logging it.</summary>
		static bool PlayQuiet(RLine r)
		{
			bool ok = true;
			quietCheck = true;
			try { string line = r.Text.Trim(); PlayExpect(Tokens(line), line, ref ok); }
			catch { ok = false; }
			finally { quietCheck = false; }
			return ok;
		}

		static void PlayExpect(string[] t, string line, ref bool ok)
		{
			// (a last word quest=<k>: the island's further quest k, 2..9, as the recipe's quest2..quest9; else the main quest)
			int qn = 0;
			if (t.Length > 2 && t[t.Length - 1].StartsWith("quest=", StringComparison.OrdinalIgnoreCase))
			{
				qn = Mathf.Clamp((int)F(t[t.Length - 1].Substring(6)) - 1, 0, 8);
				line = line.Substring(0, line.LastIndexOf(t[t.Length - 1])).TrimEnd();
				t = t.Take(t.Length - 1).ToArray();
			}
			string what = t.Length > 1 ? t[1].ToLowerInvariant() : "";
			IslandQuest q = QuestTracker.QuestOf(playEntry, qn);
			int step = QuestTracker.StepOf(playEntry, qn);
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
				case "blueprints":
				{
					// expect blueprints: every one of Raft's story-island blueprints (raft_blueprints.txt) learned or picked up in this play - or
					// lying on one of Raft's story islands the world's plan keeps (the test doesn't sail to those)
					NotePlayBlueprints(RAPI.GetLocalPlayer());
					List<string> notYet = StoryBlueprintsNotLearned().Where(b => !playBlueprints.Contains(b.Split(' ')[0])).ToList();
					List<string> kept = WorldDirector.Plan != null ? PlanChecker.Progression(WorldDirector.Plan, false).Kept : new List<string>();
					List<string> onKept = notYet.Where(b => kept.Contains(b)).ToList();
					notYet = notYet.Where(b => !kept.Contains(b)).ToList();
					Check(ref ok, notYet.Count == 0, "all of Raft's story blueprints learned" + (onKept.Count > 0 ? " or on Raft's story islands the plan keeps (" + onKept.Count + ")" : "") + (notYet.Count > 0 ? " - never: " + string.Join(", ", notYet.ToArray()) : ""));
					break;
				}
				case "survivor":
					Check(ref ok, Survivors.Has(t[2]), "survivor '" + t[2] + "' rescued (" + Survivors.All.Count + " on the list)");
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
				case "hurt":
				case "unhurt":
				{
					// expect hurt [min] / expect unhurt: the health hazard zones took from the player since the play's start or the
					// last of these (Hazards.Taken, measured round each hit). 11-05: the acid run passed with 0 taken - its health
					// watch had ended before the play began; a hazard play now checks itself
					float took = Hazards.Taken - playHurtFrom, min = what == "hurt" ? (t.Length > 2 ? F(t[2]) : 1f) : 0f;
					bool hurtOk = what == "hurt" ? took >= min : took <= 0.001f;
					if (!quietCheck) playHurtFrom = Hazards.Taken;
					Check(ref ok, hurtOk, "hazards took " + took.ToString("F2", CultureInfo.InvariantCulture) + " health" + (what == "hurt" ? " (at least " + min.ToString("F1", CultureInfo.InvariantCulture) + ")" : " (none wanted)"));
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
					// (harmless sea animals with no quest step stay in Raft's own sea events: when Raft's dolphin event ends it
					// sends them away, any time, and the island keeps them as "taken away" - not a fault. Highmoor Lodge's two
					// dolphins went the moment the island loaded in Silver Screen Seas, 11-07)
					int strayed = playEntry != null && playEntry.Root != null ? playEntry.Root.GetComponentsInChildren<CreatureSpawnPoint>(true)
						.Where(p => p.Kind != null && p.Kind.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && (p.Kind.Type == AI_NetworkBehaviourType.Dolphin || p.Kind.Type == AI_NetworkBehaviourType.Turtle || p.Kind.Type == AI_NetworkBehaviourType.Stingray || p.Kind.Type == AI_NetworkBehaviourType.Whale)).Sum(p => p.Strayed) : 0;
					int want = (int)F(t[t.Length - 1]);
					if (n < want && n + strayed >= want) Log("  " + (want - n) + " " + label + "(s) taken away by Raft's own sea event (harmless, back with the island): counted");
					Check(ref ok, n == want || (n < want && n + strayed >= want), "animals '" + label + "' alive: " + n + (strayed > 0 ? " + " + strayed + " taken away by Raft" : "") + " (expected " + t[t.Length - 1] + ")");
					// (each one's place in recipe coordinates and its gap over the floor under it, kept for animalsmoved)
					List<Vector3> at = new List<Vector3>();
					foreach (AI_NetworkBehaviour a in ScAnimals(playEntry, label))
					{
						Vector3 w = a.transform.position;
						at.Add(w);
						float gap = AnimalFloorGap(a);
						Vector3 r = w - playEntry.Position;
						Log("  " + label + " at " + (r.x + playOffset.x).ToString("0.0") + " " + (r.z + playOffset.y).ToString("0.0") + " y " + w.y.ToString("0.00")
							+ ", over its floor " + (gap < 0 ? "nothing within 3 m" : gap.ToString("0.00") + " m"));
						if (a.GetComponentInChildren<UnityEngine.AI.NavMeshAgent>(true) != null) Check(ref ok, gap >= 0 && gap < 0.6f, label + " stands on its floor (gap " + gap.ToString("0.00") + ", under 0.6)");
					}
					animalsSeen[label] = at;
					Dictionary<AI_NetworkBehaviour, Vector3> st = new Dictionary<AI_NetworkBehaviour, Vector3>();
					foreach (AI_NetworkBehaviour a in ScAnimals(playEntry, label)) if (a != null) st[a] = a.transform.position;
					animalsStart[label] = st;
					animalsFurthest[label] = st.Keys.ToDictionary(a => a, a => 0f);
					int sr;
					animalsSampleRun.TryGetValue(label, out sr);
					animalsSampleRun[label] = ++sr;
					DynamicIslands.instance.StartCoroutine(AnimalsSampler(label, sr));
					break;
				}
				case "animalsinside":
				{
					// (expect animalsinside <label> <n>: n of them have rock or a roof over them within 12 m - in the cave, not
					// on the hill over it: a herd's ring in a narrow tunnel put rats on the slope outside, standing on a floor - 11-05)
					string label = string.Join(" ", t.Skip(2).Take(t.Length - 3).ToArray());
					int inside = 0;
					foreach (AI_NetworkBehaviour a in ScAnimals(playEntry, label))
					{
						bool roof = false;
						foreach (RaycastHit h in Physics.RaycastAll(a.transform.position + Vector3.up * 0.3f, Vector3.up, 12f, ~0, QueryTriggerInteraction.Ignore))
							if (!h.collider.transform.IsChildOf(a.transform)) { roof = true; break; }
						if (roof) inside++;
						else Log("  " + label + " with open sky over it at " + (a.transform.position - playEntry.Position + new Vector3(playOffset.x, 0f, playOffset.y)).ToString("F1"));
					}
					Check(ref ok, inside == (int)F(t[t.Length - 1]), "animals '" + label + "' under a roof: " + inside + " (expected " + t[t.Length - 1] + ")");
					break;
				}
				case "animalsmoved":
				{
					// (expect animalsmoved <label> <metres>: the one that went furthest from its own start since the last expect
					// animals of it - the furthest it got, sampled, not only where it is now: fix1107)
					string label = string.Join(" ", t.Skip(2).Take(t.Length - 3).ToArray());
					List<Vector3> was;
					float most = 0f;
					if (animalsSeen.TryGetValue(label, out was))
					{
						AnimalsSample(label);
						Dictionary<AI_NetworkBehaviour, float> far;
						if (animalsFurthest.TryGetValue(label, out far) && far.Count > 0) most = far.Values.Max();
					}
					Check(ref ok, was != null && most >= F(t[t.Length - 1]), "animals '" + label + "' moved " + most.ToString("0.0") + " m (at least " + t[t.Length - 1] + ")");
					break;
				}
				case "height":
				{
					// expect height h: the player is about this high above the sea (carried up by a lift)
					Network_Player me = RAPI.GetLocalPlayer();
					float hh = me != null ? me.transform.position.y - playEntry.Position.y : -99f;
					Check(ref ok, Mathf.Abs(hh - F(t[2])) < 1.2f, "the player " + hh.ToString("F2") + " m above the sea (expected " + t[2] + ")");
					break;
				}
				case "stand":
				{
					// expect stand x z h=: what a player stands on there is about this high above the sea (a floor, a deck)
					// (below=: looked for under that height above the sea - a room's floor under its roof)
					Vector3 p = PlayPoint(F(t[2]), F(t[3]));
					var o = Options(t.Skip(4));
					float top = PlaySurface(p, o.ContainsKey("below") ? playEntry.Position.y + F(o["below"]) : 400f) - playEntry.Position.y, want = o.ContainsKey("h") ? F(o["h"]) : 0f;
					RaycastHit body;
					if (Physics.Raycast(new Vector3(p.x, o.ContainsKey("below") ? playEntry.Position.y + F(o["below"]) : 400f, p.z), Vector3.down, out body, 700f, ~0, QueryTriggerInteraction.Ignore)
						&& body.collider.GetComponentInParent<Network_Player>() != null)
						Log("  (a player stands there: looked past " + body.collider.name + " at " + (body.point.y - playEntry.Position.y).ToString("F2") + " m)");
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
				int animalWaits = 0, sidesteps = 0, hops = 0;
				Vector3 last = player.transform.position;
				Vector3 target = playEntry.Position + rel;
				Vector3 shifted = IslandWorldState.ShiftedBy;
				while (ScFlat(player.transform.position, target = playEntry.Position + rel) > 0.7f)
				{
					KeepAlive(player);
					// (a world shift mid-walk: the controller kept its place from before it and the next Move put the player back
					// there, 1000 m off the island - Frost Hollow in The Long Voyage, 11-07; the player goes where the shift put it)
					if (IslandWorldState.ShiftedBy != shifted)
					{
						player.transform.position = last - (IslandWorldState.ShiftedBy - shifted);
						shifted = IslandWorldState.ShiftedBy;
						Physics.SyncTransforms();
						last = player.transform.position;
						Log("  (the world shifted during the walk: the player kept its place on the island)");
						yield return null;
						continue;
					}
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
						// (still stuck with nothing ahead, hanging a little over a slope and not grounded: a player jumps - the walker
						// hops forward too, twice at most: Starfall Crater's rim path and Crater Lair late in a plan, 2026-10-05)
						if (blocker == null && hops < 2 && total < 40f)
						{
							hops++;
							Log("  stuck with nothing ahead (grounded " + cc.isGrounded + "): a hop forward");
							for (float t = 0f; t < 0.5f; t += Time.deltaTime)
							{
								KeepAlive(player);
								cc.Move((Vector3.up * (t < 0.25f ? 3f : -3f) + dir * pc.normalSpeed) * Time.deltaTime);
								yield return null;
							}
							stuck = 0f;
							last = player.transform.position;
							continue;
						}
						result(false, "stuck at " + (now - playEntry.Position).ToString("F1") + " on the way to " + (target - playEntry.Position).ToString("F1") +
							" (in the way: " + (blocker != null ? BlockerName(blocker) : "nothing found ahead") + ")" +
							// (what the player's own state was - the walks that stall with nothing ahead, 2026-10-05)
							" [speed " + pc.normalSpeed.ToString("F2") + ", controller " + (cc.enabled ? "on" : "OFF") + ", grounded " + cc.isGrounded +
							", parent " + (player.transform.parent != null ? player.transform.parent.name : "-") + "]");
						yield break;
					}
					yield return null;
				}
			}
			result(true, "got there, " + (player.transform.position.y - playEntry.Position.y).ToString("F2") + " m above the sea");
		}

		/// <summary>The world CINewWorld last made in this session (its name).</summary>
		internal static string MadeWorld;

		/// <summary>A world the tests may empty the bag in: one named 'CI ...', or one CINewWorld made in this session (a
		/// world named otherwise to keep its bag across islands still fills up with loot - 11-09 w03: a full bag dropped
		/// Caisson Yard's zipline plans and the quest waited for them). The play's `empty` works there; an `island` line
		/// still empties the bag only in a 'CI ...' world, so a play can carry a tool on from an earlier island.</summary>
		static bool IsTestWorld()
		{
			string w = SaveAndLoad.CurrentGameFileName ?? "";
			return w.StartsWith("CI ") || (MadeWorld != null && w == MadeWorld);
		}

		/// <summary>
		/// The play's `swim`: the player moved straight at each point in turn (up and down too) with Raft's water controller at
		/// swimming speed; within 0.7 m it is reached, 2 s without headway (or 40 s in all) is stuck - what is in the way then.
		/// </summary>
		static IEnumerator PlaySwim(Network_Player player, List<Vector3> points, Action<bool, string> result)
		{
			PersonController pc = player.PersonController;
			CharacterController cc = pc.controller;
			pc.SwitchControllerType(ControllerType.Water);
			float speed = Mathf.Max(2f, pc.swimSpeed);
			foreach (Vector3 rel in points.Select(q => q - playEntry.Position).ToList())
			{
				float stuck = 0f, total = 0f;
				Vector3 last = player.transform.position;
				Vector3 target;
				while (((target = playEntry.Position + rel) - player.transform.position).magnitude > 0.7f)
				{
					KeepAlive(player);
					if (pc.controllerType != ControllerType.Water) pc.SwitchControllerType(ControllerType.Water);
					Vector3 d = target - player.transform.position;
					Vector3 dir = d.normalized;
					Vector3 flat = new Vector3(d.x, 0f, d.z);
					if (flat.sqrMagnitude > 0.01f) Look(player, Quaternion.LookRotation(flat).eulerAngles.y, 0f);
					cc.Move(dir * Mathf.Min(speed * Time.deltaTime, d.magnitude));
					Vector3 now = player.transform.position;
					stuck = (now - last).magnitude < speed * Time.deltaTime * 0.3f ? stuck + Time.deltaTime : 0f;
					last = now;
					total += Time.deltaTime;
					if (stuck > 2f || total > 40f)
					{
						RaycastHit h;
						Vector3 eye = now + Vector3.up * 0.5f;
						string what = Physics.SphereCast(eye, 0.3f, dir, out h, 1.5f, ~0, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<Network_Player>() == null
							? BlockerName(h.collider) : "nothing found ahead";
						result(false, "stuck at " + (now - playEntry.Position).ToString("F1") + " on the way to " + rel.ToString("F1") + " (in the way: " + what + ", " + pc.controllerType + ")");
						yield break;
					}
					yield return null;
				}
			}
			result(true, "got there, " + (player.transform.position.y - playEntry.Position.y).ToString("F2") + " m above the sea");
		}

		/// <summary>What stops a walking player: the nearest collider within a metre ahead at waist height, not the player's own.</summary>
		/// <summary>
		/// Whether the player, standing where the walk left them, can open a chest: within Raft's use reach of the eye (2.5 m,
		/// Player.UseDistanceDefault) with nothing in between - Vasagatan Remade's safe behind a doorway too low to walk
		/// through is opened from the doorway (the review, 2026-10-03). Null when they can, else why not.
		/// </summary>
		static string ScReach(IslandWorldState.Entry e, string title)
		{
			LootCrate chest = e.Root.GetComponentsInChildren<LootCrate>(true).FirstOrDefault(c =>
			{
				IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>();
				return r != null && ObjectProps.Get(r.Props, ObjectProps.NoteTitle).Equals(title, StringComparison.OrdinalIgnoreCase);
			});
			if (chest == null) return "no chest '" + title + "'";
			Camera cam = Camera.main;
			if (cam == null) return "no camera";
			Transform root = chest.GetComponentInParent<IslandObjectRef>().transform;
			Renderer[] parts = root.GetComponentsInChildren<Renderer>(false);
			Vector3 to = root.position;
			if (parts.Length > 0) { Bounds b = parts[0].bounds; foreach (Renderer p in parts) b.Encapsulate(p.bounds); to = b.center; }
			Vector3 eye = cam.transform.position;
			float dist = Vector3.Distance(eye, to);
			// (the chest's own colliders count whether solid or a trigger - Raft uses chests through either; anything else
			// solid on the way is in the way, other triggers - zones - are not)
			foreach (RaycastHit hit in Physics.RaycastAll(eye, (to - eye).normalized, 2.5f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
			{
				if (hit.collider.transform.IsChildOf(root)) return null;
				if (hit.collider.isTrigger || hit.collider.transform.IsChildOf(cam.transform.root)) continue;
				// (a slain creature's body (Body_Ragdoll) is loose and pushed aside, not part of the island - Sun Atoll's
				// roaches fell in front of the locker now and then, 2026-10-19)
				if (hit.collider.name.IndexOf("Ragdoll", StringComparison.OrdinalIgnoreCase) >= 0) continue;
				return hit.collider.name + " in the way " + hit.distance.ToString("F1") + " m from the eye (the chest " + dist.ToString("F1") + " m)";
			}
			return "nothing of it within 2.5 m toward it (it is " + dist.ToString("F1") + " m from the eye)";
		}

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

		/// <summary>The camera's effects that draw water drops running down the screen (Raft's WaterDropsIME), switched on.</summary>
		internal static List<Behaviour> ScreenDrops(Camera cam)
		{
			return cam.GetComponents<Behaviour>().Where(b => b != null && b.enabled && !(b is Camera) && b.GetType().Name.IndexOf("WaterDrops", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
		}

		/// <summary>What a picture shows, by name: the first solid thing hit through 5 x 3 points of the frame (left to
		/// right, top to bottom) with its distance - to name a piece seen in a shot (11-05: a flat wall and a box in The Abyss's
		/// acid shots). Logs a FRAME line per row.</summary>
		static void LogFrameHits(Camera cam)
		{
			var rows = new List<string>();
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true;
			try
			{
				Physics.SyncTransforms();
				foreach (float vy in new[] { 0.75f, 0.5f, 0.25f })
				{
					var parts = new List<string>();
					foreach (float vx in new[] { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f })
					{
						RaycastHit h;
						Ray ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0f));
						string what = "-";
						if (Physics.Raycast(ray, out h, 400f, ~0, QueryTriggerInteraction.Ignore))
						{
							Transform tr = h.collider.transform;
							what = (tr.parent != null ? tr.parent.name + "/" : "") + tr.name + " " + h.distance.ToString("F1", CultureInfo.InvariantCulture);
						}
						parts.Add(what);
					}
					rows.Add("FRAME " + (vy > 0.6f ? "top" : vy > 0.4f ? "mid" : "low") + ": " + string.Join(" | ", parts.ToArray()));
				}
			}
			catch (Exception e) { rows.Add("FRAME (" + e.Message + ")"); }
			finally { Physics.queriesHitBackfaces = backs; }
			foreach (string r in rows) Log(r);
		}

		static IEnumerator PlayPicture(string file, Vector3 from, Vector3 look, float lightM = 0f)
		{
			Camera cam = Camera.main;
			if (cam == null) yield break;
			// (CIGfx low: the user's quality for the picture, two frames for the water to follow)
			bool gfx = GfxShotBegin();
			if (gfx) { yield return null; yield return null; }
			yield return new WaitForEndOfFrame();
			int mask = cam.cullingMask;
			foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.layer != 0) cam.cullingMask &= ~(1 << r.gameObject.layer);
			Vector3 lp = cam.transform.localPosition; Quaternion lr = cam.transform.localRotation;
			// (the drops on the screen after the player came out of the water bent every picture of the play that followed a
			// dive - Shelter Atoll's storeroom and Dr. Shogo wavy, the review 11-05: off for the picture; the underwater look stays)
			List<Behaviour> drops = ScreenDrops(cam);
			foreach (Behaviour b in drops) b.enabled = false;
			byte[] pic;
			GameObject lamp = null;
			try
			{
				cam.transform.position = from;
				cam.transform.LookAt(look);
				if (lightM > 0f)
				{
					lamp = new GameObject("CIPictureLight");
					lamp.transform.position = from;
					lamp.transform.rotation = cam.transform.rotation;
					Light li = lamp.AddComponent<Light>();
					li.type = LightType.Spot; li.range = lightM; li.spotAngle = 70f; li.intensity = 1.6f; li.shadows = LightShadows.None;
				}
				pic = RenderCam(cam, 1280, 720).EncodeToJPG(88);
				LogFrameHits(cam);
			}
			finally
			{
				if (lamp != null) UnityEngine.Object.Destroy(lamp);
				cam.transform.localPosition = lp; cam.transform.localRotation = lr;
				cam.cullingMask = mask;
				foreach (Behaviour b in drops) b.enabled = true;
				GfxShotEnd(gfx);
			}
			if (drops.Count > 0) Log("  (the screen's water drops off for the picture: " + string.Join(", ", drops.Select(b => b.GetType().Name).ToArray()) + ")");
			string path = Path.Combine(RecipeFolder, "play_" + file + ".jpg");
			File.WriteAllBytes(path, pic);
			Network_Player me = RAPI.GetLocalPlayer();
			PersonController pc = me != null ? me.PersonController : null;
			Log("PICTURE " + Path.GetFullPath(path) + (pc != null ? " (the player " + pc.controllerType + " at " + (me.transform.position - (playEntry != null ? playEntry.Position : Vector3.zero)).ToString("F1") + ")" : ""));
		}
	}
}
