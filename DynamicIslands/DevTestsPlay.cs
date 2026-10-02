using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
	///   expect step n | story id n | shown name | hidden name | message text | item name n | animals label n | stand x z h=
	///   wait s | log text | hour h | picture file x y z lookx looky lookz (Raft's camera, its water - for the guide;
	///                                   heights above the sea, or "+h" above what is below)
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

		/// <summary>A point of the played island (metres from its land middle) in the world.</summary>
		static Vector3 PlayPoint(float x, float z) { return playEntry.Position + new Vector3(x, 0f, z); }

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
				if (verb != "island" && verb != "log" && verb != "wait" && verb != "hour" && playEntry == null) { Fail("play " + name + ", " + rl.Where + ": no island yet"); yield break; }
				if (playEntry != null && playEntry.Root == null && verb != "log") { Fail("play " + name + ", " + rl.Where + ": the island isn't loaded"); yield break; }
				KeepAlive(me);
				switch (verb)
				{
					case "island":
					{
						string island = Rest(line, 1).Replace(" keep", "").Trim();
						keep = line.EndsWith(" keep");
						// (a copy left by an earlier run that stopped half way: removed first)
						var left = IslandWorldState.Islands.Where(x => string.Equals(x.HostName, island, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList();
						if (left.Count > 0) { IslandWorldState.RemoveIds(left, true); IslandCache.Forget(); Log("  (removed " + left.Count + " copy/copies of '" + island + "' left by an earlier run)"); yield return new WaitForSeconds(1f); }
						Vector3? spot = ScSpot(island, 400f);
						if (!spot.HasValue) { Fail("play " + name + ": no open sea for '" + island + "'"); yield break; }
						yield return ScBring(island, spot.Value, made);
						playEntry = made.LastOrDefault();
						if (playEntry == null || playEntry.Root == null) { Fail("play " + name + ": '" + island + "' didn't come"); yield break; }
						yield return new WaitForSeconds(3f);
						// (a clean start: the crew holds none of the island's story items - an earlier run in this world left them)
						foreach (StoryItemDef d in StoryItems.Of(IslandCache.PropsOf(playEntry)))
							if (StoryBook.Count(d.Id) > 0) StoryBook.Take(d.Id, StoryBook.Count(d.Id));
						Log("  '" + island + "' is in the world at " + playEntry.Position.ToString("F0"));
						break;
					}
					case "stand":
						yield return StandRoutine(playEntry.Root);
						break;
					case "at":
					{
						Vector3 p = PlayPoint(F(t[1]), F(t[2]));
						p.y = opt.ContainsKey("h") ? playEntry.Position.y + F(opt["h"]) : PlaySurface(p) + 0.2f + (opt.ContainsKey("y") ? F(opt["y"]) : 0f);
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
						if (grip == null || ScFlat(grip.bounds.center, p) > 3f) { Check(ref ok, false, "a ladder near " + t[1] + "," + t[2] + (grip != null ? " (the nearest is " + ScFlat(grip.bounds.center, p).ToString("F1") + " m off)" : "")); checks++; break; }
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
					case "expect":
						checks++;
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
			if (ok) Log("PASS: play " + name + " (" + checks + " checks)"); else Fail("play " + name);
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
		/// 0.7 m) before it has been stuck for 2 s.
		/// </summary>
		static IEnumerator PlayWalk(Network_Player player, List<Vector3> points, Action<bool, string> result)
		{
			PersonController pc = player.PersonController;
			CharacterController cc = pc.controller;
			pc.SwitchControllerType(ControllerType.Ground);
			foreach (Vector3 target in points)
			{
				float stuck = 0f, total = 0f;
				Vector3 last = player.transform.position;
				while (ScFlat(player.transform.position, target) > 0.7f)
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
						result(false, "stuck at " + (now - playEntry.Position).ToString("F1") + " on the way to " + (target - playEntry.Position).ToString("F1"));
						yield break;
					}
					yield return null;
				}
			}
			result(true, "got there, " + (player.transform.position.y - playEntry.Position.y).ToString("F2") + " m above the sea");
		}

		/// <summary>A picture with Raft's own camera (its water and light, no HUD, no held tool) for the guide:
		/// Mods\DynamicIslands\recipes\play_&lt;file&gt;.jpg 1280x720.</summary>
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
			Log("PICTURE " + Path.GetFullPath(path));
		}
	}
}
