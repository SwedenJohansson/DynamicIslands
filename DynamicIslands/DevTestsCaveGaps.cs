using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	// Cave gaps (content round 11-04): the recipe's dig step (G1) and the checks that the cave tools' caves need.
	public static partial class DevTests
	{
		const string RecipeDigIsland = "citest-recipedig";

		[ConsoleCommand(name: "CIRecipeDig", docs: "Dev, the editor (opened when needed): G1 - a recipe's dig step. A recipe generates an island; a second one digs a tunnel into a hill's foot (dig 0,0 ... as=group), a branch pressed on its passage (a first point x,z over it) and saves the island; the tunnel has 2 strokes, its path in its props, the ground cut at its mouth, the group; a boulder reaching into the passage from its side is taken away with the rock (11-04); loaded again it is there; a third recipe fills the branch's end (dig ... fill); bad dig lines are refused with a reason. Leaves citest-recipedig saved")]
		public static void RecipeDigCommand(string[] args) { StartTest(RecipeDigRoutine()); }

		static IEnumerator RecipeDigRoutine()
		{
			bool ok = true;
			string pa = Path.Combine(RecipeFolder, "ci_dig_a.recipe"), pb = Path.Combine(RecipeFolder, "ci_dig_b.recipe"), pc = Path.Combine(RecipeFolder, "ci_dig_c.recipe");
			try
			{
				Directory.CreateDirectory(RecipeFolder);
				File.WriteAllLines(pa, new[]
				{
					"# G1 check (CIRecipeDig): the island",
					"new",
					"gen Seed=4242 Radius=130 Height=30 Style=0",
					"generate",
				});
			}
			catch (Exception e) { Fail("recipe dig: writing " + pa + ": " + e.Message); yield break; }
			yield return RecipeRoutine("ci_dig_a");
			yield return new WaitForSecondsRealtime(0.6f);
			Terrain t = terraineditor.terrain;
			if (t == null) { Fail("recipe dig: no island after the first recipe"); yield break; }
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorWaterLevel;
			Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			int open0 = OpenCells(data);

			// A hill's foot (as CIDigTunnelEditor finds it)
			Vector3 mouth = Vector3.zero, dir = Vector3.zero, side = Vector3.zero;
			bool found = false;
			float bestScore = 0f;
			foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
				for (float s = 90f; s > 10f; s -= 2f)
				{
					Vector3 p = ground(c - d * s);
					if (p.y < sea + 1.5f) continue;
					foreach (Vector3 sd in new[] { Vector3.Cross(Vector3.up, d), -Vector3.Cross(Vector3.up, d) })
					{
						float score = Mathf.Min(Mathf.Min(ground(p + d * 10f).y - p.y - 3f, ground(p + d * 24f).y - p.y - 5.5f),
							Mathf.Min(ground(p + d * 24f + sd * 10f).y - p.y - 5.5f, ground(p + d * 16f + sd * 8f).y - p.y - 5f));
						if (score >= bestScore) { bestScore = score; mouth = p; dir = d; side = sd; found = true; }
					}
				}
			if (!found) { Fail("recipe dig: no hillside on the generated island"); yield break; }
			Log("  the mouth " + (mouth.y - sea).ToString("0.0") + " m above the sea at " + mouth.x.ToString("0.0") + " " + mouth.z.ToString("0.0") + ", dug " + dir + " then " + side);
			Func<Vector3, string> xz = v => v.x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "," + v.z.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
			Vector3 a = dir * 24f, b = dir * 24f + side * 10f, br = dir * 12f, brEnd = dir * 12f - side * 8f;
			string inv(float f) { return f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
			try
			{
				File.WriteAllLines(pb, new[]
				{
					"# G1 check (CIRecipeDig): a tunnel, a branch from it, saved",
					"origin " + inv(mouth.x) + " " + inv(mouth.z),
					"dig 0,0 " + xz(a) + " " + xz(b) + " r=1.75 as=cidig",
					"dig " + xz(br) + " " + xz(brEnd),
					"save " + RecipeDigIsland,
				});
			}
			catch (Exception e) { Fail("recipe dig: writing " + pb + ": " + e.Message); yield break; }
			yield return RecipeRoutine("ci_dig_b");
			yield return null;
			DigTunnel[] tuns = UnityEngine.Object.FindObjectsOfType<DigTunnel>();
			DigTunnel tun = tuns.FirstOrDefault();
			Check(ref ok, tuns.Length == 1, "one dug tunnel on the island (" + tuns.Length + ")");
			if (tun == null) { Fail("recipe dig"); yield break; }
			int n1 = tun.PointCount;
			Check(ref ok, tun.Strokes.Count == 2 && n1 >= 30, "the passage and its branch: " + tun.Strokes.Count + " strokes, " + n1 + " points");
			Vector3 first = DigLine(tun).First();
			Check(ref ok, new Vector2(first.x - mouth.x, first.z - mouth.z).magnitude < 1.5f, "pressed at the hill's foot (" + new Vector2(first.x - mouth.x, first.z - mouth.z).magnitude.ToString("0.0") + " m off)");
			if (tun.Strokes.Count > 1)
			{
				Vector3 bs = DigLine(tun, 1).First(), be = DigLine(tun, 1).Last();
				Vector3 want = mouth + br;
				Check(ref ok, new Vector2(bs.x - want.x, bs.z - want.z).magnitude < 2.5f && new Vector2(be.x - (mouth + brEnd).x, be.z - (mouth + brEnd).z).magnitude < 2.5f,
					"the branch from the passage 12 m in to 8 m aside (starts " + new Vector2(bs.x - want.x, bs.z - want.z).magnitude.ToString("0.0") + " m off, ends " + new Vector2(be.x - (mouth + brEnd).x, be.z - (mouth + brEnd).z).magnitude.ToString("0.0") + " m off)");
			}
			EditorGameObject ego = tun.GetComponent<EditorGameObject>();
			Check(ref ok, ego != null && ego.Props != null && ego.Props.ContainsKey(DigTunnel.PathKey) && ego.Props[DigTunnel.PathKey].Length > 20, "the path kept in its props");
			List<EditorGameObject> grp;
			Check(ref ok, recipeGroups.TryGetValue("cidig", out grp) && grp.Count == 1 && grp[0] == ego, "as=cidig: the tunnel in the group (" + (grp != null ? grp.Count : 0) + ")");
			DigTunnel.EditorNow(t);
			int cut = DigTunnel.EditorCutCells.Count;
			Check(ref ok, cut > 0 && OpenCells(data) >= open0 + cut, "the ground cut at its mouth: " + cut + " cells");
			Check(ref ok, RecipeSaved == RecipeDigIsland, "saved as " + RecipeDigIsland + " (" + (RecipeSaved ?? "not saved") + ")");

			// The brush takes away what reaches into the passage (11-04: a boulder on a chamber's rim hung into it - only objects
			// within 0.8 of the radius of a point went before)
			{
				string pp = Path.Combine(RecipeFolder, "ci_dig_p.recipe");
				List<Vector3> line0 = DigLine(tun);
				Vector3 at18 = mouth + dir * 18f;
				Vector3 lc = line0.OrderBy(p => new Vector2(p.x - at18.x, p.z - at18.z).sqrMagnitude).First();
				try { File.WriteAllLines(pp, new[] { "# G1 check (CIRecipeDig): a boulder beside the passage", "origin " + inv(lc.x) + " " + inv(lc.z), "place SmallBoulder4 ciboulder at 0 0 h=" + inv(lc.y - sea) + " centred" }); }
				catch (Exception ex) { Fail("recipe dig: writing " + pp + ": " + ex.Message); yield break; }
				yield return RecipeRoutine("ci_dig_p");
				yield return null;
				try { File.Delete(pp); } catch { }
				EditorGameObject bo = SelectionTools.Placed().Where(x => x != null && x.GameObjectName == "SmallBoulder4").OrderBy(x => (x.transform.position - lc).sqrMagnitude).FirstOrDefault();
				Collider[] bc = bo != null ? bo.GetComponentsInChildren<Collider>().Where(x => !x.isTrigger).ToArray() : new Collider[0];
				if (bc.Length == 0) Check(ref ok, false, "a boulder placed beside the passage (" + (bo == null ? "not placed" : "no collider") + ")");
				else
				{
					Physics.SyncTransforms();
					Bounds bb = bc[0].bounds;
					foreach (Collider x in bc) bb.Encapsulate(x.bounds);
					float half = Mathf.Abs(Vector3.Dot(bb.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))));
					bo.transform.position += lc + side * (0.9f * 1.75f + half) - bb.center;
					Physics.SyncTransforms();
					bb = bc[0].bounds;
					foreach (Collider x in bc) bb.Encapsulate(x.bounds);
					float near = (bb.ClosestPoint(lc) - lc).magnitude;
					bool gone = DigTunnel.InsideObjects(tun).Contains(bo.gameObject);
					Check(ref ok, gone && near > 0.8f * 1.75f, "a boulder (its middle " + (bb.center - lc).magnitude.ToString("0.0") + " m off) reaching " + (1.75f - near).ToString("0.00") + " m into the passage's side: " + (gone ? "taken away with the rock" : "LEFT standing in the passage"));
					bo.gameObject.SetActive(false);
				}
			}

			// Loaded again
			DynamicIslands.NewIsland();
			yield return null;
			yield return new WaitForSecondsRealtime(0.7f);
			DynamicIslands.LoadIsland(RecipeDigIsland);
			yield return null;
			yield return null;
			yield return new WaitForSecondsRealtime(0.7f);
			t = terraineditor.terrain;
			DigTunnel back = GameObject.Find("PlacedObjects").GetComponentsInChildren<DigTunnel>(false).FirstOrDefault();
			int cutBack = DigTunnel.EditorNow(t);
			Check(ref ok, back != null && back.PointCount == n1 && back.Strokes.Count == 2 && cutBack == cut, "loaded again: " + (back != null ? back.PointCount + " points in " + back.Strokes.Count + " strokes, " : "no tunnel, ") + cutBack + " cells cut");

			// A fill at the branch's end (Shift); bad lines refused
			if (back != null)
			{
				Vector3 end = DigLine(back, 1).Last() - mouth;
				try { File.WriteAllLines(pc, new[] { "# G1 check (CIRecipeDig): the branch's end filled", "origin " + inv(mouth.x) + " " + inv(mouth.z), "dig " + xz(end) + " fill" }); }
				catch (Exception e) { Fail("recipe dig: writing " + pc + ": " + e.Message); yield break; }
				yield return RecipeRoutine("ci_dig_c");
				yield return null;
				Check(ref ok, back.PointCount < n1, "dig ... fill: the branch's end filled in (" + n1 + " -> " + back.PointCount + " points)");
			}
			string err;
			foreach (string bad in new[] { "dig 0,0", "dig 0,0 5,5 r=9", "dig 0,0 5,5 slope=60", "dig 0,0 zz 5,5", "dig 400,400 fill" })
			{
				DigStep(Tokens(bad), out err);
				Check(ref ok, err != null, "refused: '" + bad + "' (" + (err ?? "taken") + ")");
			}
			foreach (string p in new[] { pa, pb, pc }) try { File.Delete(p); } catch { }
			if (ok) Log("PASS: recipe dig"); else Fail("recipe dig");
		}

		/// <summary>
		/// A dug tunnel's rim rock standing out of the ground apart from its mouth (11-06: Ironreef's tunnel B had flat grey
		/// shards in the grass beside it): from over the ground round each opening (3 m out, every 0.2 m) a ray down on the
		/// tunnel's own rock; where it meets the rock over the ground, that rock must join the mouth (over a cut cell) through
		/// more such rock. Returns how many points of loose rock (0: none); detail says how many and where the first is.
		/// </summary>
		static int LooseRim(DigTunnel t, out string detail)
		{
			detail = "no rim";
			Transform rockT = t != null ? t.transform.Find(DigTunnel.RockName) : null;
			MeshCollider mc = rockT != null ? rockT.GetComponent<MeshCollider>() : null;
			Terrain land = t != null ? t.Land() : null;
			if (mc == null || land == null || t.Openings.Count == 0) return 0;
			const float step = 0.2f, reach = 3f, over = 0.05f;
			TerrainData td = land.terrainData;
			float cell = Mathf.Max(td.size.x, td.size.z) / td.holesResolution;
			float x0 = t.Openings.Min(o => o.x) - reach, z0 = t.Openings.Min(o => o.y) - reach;
			int nx = Mathf.CeilToInt((t.Openings.Max(o => o.x) + reach - x0) / step) + 1, nz = Mathf.CeilToInt((t.Openings.Max(o => o.y) + reach - z0) / step) + 1;
			var up = new bool[nx * nz];
			var seen = new bool[nx * nz];
			var todo = new Queue<int>();
			for (int z = 0; z < nz; z++)
				for (int x = 0; x < nx; x++)
				{
					var at = new Vector2(x0 + x * step, z0 + z * step);
					float near = t.Openings.Min(o => (o - at).sqrMagnitude);
					if (near > reach * reach) continue;
					float g = land.SampleHeight(new Vector3(at.x, 0f, at.y)) + land.transform.position.y;
					RaycastHit hit;
					if (!mc.Raycast(new Ray(new Vector3(at.x, g + 6f, at.y), Vector3.down), out hit, 12f) || hit.point.y < g + over) continue;
					int i = x + z * nx;
					up[i] = true;
					if (near <= cell * cell) { seen[i] = true; todo.Enqueue(i); } // (over a cut cell: the mouth's own rock)
				}
			while (todo.Count > 0)
			{
				int i = todo.Dequeue(), x = i % nx, z = i / nx;
				for (int dz = -1; dz <= 1; dz++)
					for (int dx = -1; dx <= 1; dx++)
					{
						int ax = x + dx, az = z + dz;
						if (ax < 0 || az < 0 || ax >= nx || az >= nz) continue;
						int j = ax + az * nx;
						if (!up[j] || seen[j]) continue;
						seen[j] = true;
						todo.Enqueue(j);
					}
			}
			int loose = 0, rim = 0;
			Vector2 first = Vector2.zero;
			for (int i = 0; i < up.Length; i++)
			{
				if (!up[i]) continue;
				if (seen[i]) { rim++; continue; }
				if (loose++ == 0) first = new Vector2(x0 + (i % nx) * step, z0 + (i / nx) * step);
			}
			detail = loose == 0 ? "rim " + rim + " points over the ground, none loose"
				: loose + " points of loose rim rock over the ground apart from the mouth, first at " + first.ToString("F1");
			return loose;
		}

		/// <summary>
		/// A dug tunnel seen from where a player stands in it (G3, the X7 sweep): eyes 1.6 m over the floor every 4 points along
		/// each of its strokes, 81 looks each (DigLeaks) - none to the sky, out through the hill or to the ground from below. A
		/// joined system: the other tunnels' passages count as inside, a cave piece's rock met from the front as the cave's. The
		/// looks that see through (0: none); detail: how many, and the worst place; eye/look: the first such look.
		/// </summary>
		static int DugSeeThrough(DigTunnel t, IList<DigTunnel> joined, out string detail, out Vector3 eye, out Vector3 look)
		{
			eye = look = Vector3.zero;
			Terrain land = t != null ? t.Land() : null;
			if (t == null || land == null || t.Strokes == null) { detail = "no ground under it"; return 0; }
			var eyes = new List<Vector3>();
			int noFloor = 0;
			for (int k = 0; k < t.Strokes.Count; k++)
			{
				List<Vector3> line = DigLine(t, k);
				for (int i = k == 0 ? 2 : 3; i < line.Count; i += 4)
				{
					RaycastHit fh;
					if (DigFloor(line[i], out fh) && (IsDigRock(fh.collider) || CaveRock(fh.collider))) eyes.Add(fh.point + Vector3.up * 1.6f);
					else noFloor++;
				}
			}
			var seen = new List<string>();
			var rays = new List<Ray>();
			int under;
			int leaks = DigLeaks(t, land, eyes, seen, out under, joined, CaveRock, rays);
			if (rays.Count > 0) { eye = rays[0].origin; look = rays[0].direction; }
			else if (eyes.Count > 1) { eye = eyes[eyes.Count / 2]; look = (eyes[0] - eye).normalized; }
			detail = t.Strokes.Count + " stroke(s), " + t.PointCount + " points, " + eyes.Count + " eyes (" + noFloor + " without a floor): " + leaks + " looks out, " + under + " to the ground from below"
				+ (seen.Count > 0 ? ": " + string.Join("; ", seen.Take(3).ToArray()) : "");
			return leaks + under;
		}
	

		// G4: Raft's six characters as islanders (islander.character), first use
		static readonly string[] SixCharacters = { "Johnny", "Elaine", "Tala", "Shogo", "Maya", "Rouhi" };
		static readonly string[] SixPoses = { "stand", "sit", "fish", "sleep", "lean", "stand" };

		[ConsoleCommand(name: "CIIslanderChars", docs: "Dev, in a world: G4 - Raft's characters as islanders. No part (host): every character Raft has logged (index, name), the six names Johnny, Elaine, Tala, Shogo, Maya, Rouhi each read by islander.character (any case, or the index) to its own character; 'ciplayer' spawned with the six in a row (stand, sit, fish, sleep, lean, stand): each shows its character, talks with its name; pictures shot_chars_*.png. Parts for two players: setup (host: the island kept), check (either player: the models and a talk on this machine; 'CHARS host|client: ...'), end (host: removed)")]
		public static void IslanderCharsCommand(string[] args) { StartTest(IslanderCharsRoutine(args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "")); }

		static IEnumerator IslanderCharsRoutine(string part)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || RAPI.GetLocalPlayer() == null) { Fail("islander chars: run in a world"); yield break; }
			bool ok = true;
			if (part == "end")
			{
				RemovePlayerIsland(IslandWorldState.Islands.FirstOrDefault(i => i.HostName == PlayerIsland));
				Log("PASS: islander chars end");
				yield break;
			}
			if (part == "check")
			{
				IslandWorldState.Entry here = IslandWorldState.Islands.FirstOrDefault(i => i.Root != null && i.HostName == PlayerIsland);
				if (here == null) { Fail("islander chars: '" + PlayerIsland + "' is not loaded here (" + GupRole + ")"); yield break; }
				yield return CharsCheck(here, GupRole, false, r => ok = r);
				if (ok) Log("PASS: islander chars " + GupRole); else Fail("islander chars " + GupRole);
				yield break;
			}
			if (!Raft_Network.IsHost) { Fail("islander chars: the host spawns the island"); yield break; }
			yield return EnsureAlive();

			// The names Raft has
			List<SO_Character> all = Behaviours.Characters();
			Log("  Raft's characters (" + all.Count + "): " + string.Join(", ", all.Where(c => c != null).Select(c => c.index + "=" + c.displayName + (c.modelPrefab == null ? " (no model)" : "")).ToArray()));
			var got = new List<SO_Character>();
			foreach (string n in SixCharacters)
			{
				SO_Character c = Behaviours.CharacterOf(n), lower = Behaviours.CharacterOf(" " + n.ToLowerInvariant() + " ");
				SO_Character byIndex = c != null ? Behaviours.CharacterOf(c.index.ToString()) : null;
				Check(ref ok, c != null && c.modelPrefab != null && lower == c && byIndex == c, "islander.character=" + n + ": " + (c != null ? "'" + c.displayName + "' (index " + c.index + ")" + (c.modelPrefab != null ? "" : ", no model") + (lower == c ? "" : ", lower case not read") : "not known"));
				got.Add(c);
			}
			Check(ref ok, got.Where(c => c != null).Distinct().Count() == SixCharacters.Length, "six different characters");
			Check(ref ok, Behaviours.CharacterOf("Nobody") == null && Survivors.CharacterFor("Nobody", "x/1") != null, "an unknown name: a character picked for it, no error");

			IslandFile f = PlayerTestIsland((file, add) =>
			{
				for (int i = 0; i < SixCharacters.Length; i++)
					add(Islanders.ObjectName, (i - 2.5f) * 3f, 5f, P(BehaviourProps.Name, SixCharacters[i], Islanders.PoseProp, SixPoses[i], Islanders.CharacterProp, i == 5 ? SixCharacters[i].ToUpperInvariant() : SixCharacters[i],
						Islanders.TextProp, "I am " + SixCharacters[i] + "."));
				file.Props[IslandQuest.KeyTitle] = "CI characters";
			});
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, x => e = x);
			if (e == null || e.Root == null) { Fail("islander chars: the island did not spawn"); RemovePlayerIsland(e); yield break; }
			bool keep = part == "setup";
			try
			{
				yield return new WaitForSeconds(1.5f);
				yield return CharsCheck(e, "host", true, r => ok &= r);
			}
			finally
			{
				DialoguePanel.Close();
				if (!keep) RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: islander chars" + (keep ? " setup (island " + e.Id + ")" : "")); else Fail("islander chars");
		}

		/// <summary>The six islanders on this machine: each shows its own character, talks with its name; pictures when shots.</summary>
		static IEnumerator CharsCheck(IslandWorldState.Entry e, string role, bool shots, Action<bool> result)
		{
			bool ok = true;
			var line = new List<string>();
			Islander[] isls = e.Root.GetComponentsInChildren<Islander>(true);
			Check(ref ok, isls.Length == SixCharacters.Length, role + ": six islanders (" + isls.Length + ")");
			Vector3 sum = Vector3.zero;
			Islander first = null;
			foreach (string n in SixCharacters)
			{
				Islander isl = isls.FirstOrDefault(x => x.Ref != null && Islanders.NameOf(x.Ref.Props) == n);
				SO_Character want = Behaviours.CharacterOf(n);
				bool right = isl != null && isl.HasModel && want != null && isl.Character == want;
				Check(ref ok, right, role + ": " + n + " shows " + (isl == null ? "nothing" : !isl.HasModel ? "the marker figure" : isl.Character != null ? isl.Character.displayName : "?"));
				line.Add(n + "=" + (right ? "ok" : "bad"));
				if (isl == null) continue;
				sum += isl.transform.position;
				if (first == null) first = isl;
				PutPlayerNear(isl.transform, 2f);
				yield return new WaitForSeconds(0.3f);
				isl.Talk();
				yield return new WaitForSeconds(0.3f);
				bool talks = DialoguePanel.IsOpen && DialoguePanel.ShownSpeaker == n && DialoguePanel.ShownText == "I am " + n + ".";
				Check(ref ok, talks, role + ": " + n + " talks (" + (DialoguePanel.ShownSpeaker ?? "no dialogue") + ": " + (DialoguePanel.ShownText ?? "") + ")");
				DialoguePanel.Close();
				yield return new WaitForSeconds(0.2f);
			}
			Log("CHARS " + role + ": " + string.Join(" ", line.ToArray()));
			if (shots && first != null)
			{
				Vector3 mid = sum / SixCharacters.Length;
				Vector3 fwd = first.transform.forward; fwd.y = 0f;
				if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
				fwd.Normalize();
				Vector3 along = Vector3.Cross(Vector3.up, fwd);
				SetHour(12f);
				yield return new WaitForSeconds(0.5f);
				yield return CameraShot(mid + fwd * 9f + Vector3.up * 2f, mid + Vector3.up * 0.9f, "chars_all");
				yield return CameraShot(mid + fwd * 4.5f + along * 4.5f + Vector3.up * 1.6f, mid + along * 4.5f + Vector3.up * 0.9f, "chars_a");
				yield return CameraShot(mid + fwd * 4.5f - along * 4.5f + Vector3.up * 1.6f, mid - along * 4.5f + Vector3.up * 0.9f, "chars_b");
			}
			result(ok);
		}
	

		// G2 and the look checks: test islands made with the Dig brush and the cave tool (CICaveGapsBuild), then seen in a world
		// (CICaveGapsLook): pictures from outside and inside by day and night, walked in and back out, the flooded part water
		// and the raised room dry with breath coming back.
		static readonly string[] GapIslands = { "cgaps-dig", "cgaps-st", "cgaps-uw" };
		static string CgInv(float f) { return f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
		static string XZ(Vector3 v) { return CgInv(v.x) + "," + CgInv(v.z); }
		static string XZs(Vector3 v) { return CgInv(v.x) + " " + CgInv(v.z); }

		/// <summary>The foot of a hill dug into along d (toward the island's middle): the best place minOut-90 m out; score:
		/// how much rock over a 24 m way in at its thinnest.</summary>
		static bool HillFoot(Terrain t, Vector3 d, float minOut, out Vector3 mouth, out Vector3 side, out float score)
		{
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorSeaInWorld;
			Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			mouth = side = Vector3.zero; score = float.MinValue;
			bool found = false;
			for (float s = 90f; s > minOut; s -= 2f)
			{
				Vector3 p = ground(c - d * s);
				if (p.y < sea + 1.5f) continue;
				foreach (Vector3 sd in new[] { Vector3.Cross(Vector3.up, d), -Vector3.Cross(Vector3.up, d) })
				{
					float sc = Mathf.Min(Mathf.Min(ground(p + d * 10f).y - p.y - 3f, ground(p + d * 24f).y - p.y - 5.5f),
						Mathf.Min(ground(p + d * 24f + sd * 10f).y - p.y - 5.5f, ground(p + d * 16f + sd * 8f).y - p.y - 5f));
					if (sc > score) { score = sc; mouth = p; side = sd; found = true; }
				}
			}
			return found;
		}

		/// <summary>The sea floor depth m under the sea on the way in along d (toward the middle): the outermost such point
		/// whose way in climbs above the sea within 45 m; climb: how far in that is.</summary>
		static bool Seabed(Terrain t, Vector3 d, float depth, out Vector3 at, out float climb)
		{
			TerrainData data = t.terrainData;
			Vector3 o = t.transform.position, c = o + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);
			float sea = DynamicIslands.EditorSeaInWorld;
			Func<Vector3, Vector3> ground = p => { p.y = t.SampleHeight(p) + o.y; return p; };
			at = Vector3.zero; climb = 999f;
			for (float s = 20f; s < data.size.x / 2f - 5f; s += 1f)
			{
				Vector3 p = ground(c - d * s);
				if (p.y > sea - depth) continue;
				for (float k = 1f; k < 45f; k += 1f)
					if (ground(p + d * k).y > sea + 1f) { at = p; climb = k; return true; }
				return false;
			}
			return false;
		}

		[ConsoleCommand(name: "CICaveGapsBuild", docs: "Dev, the editor: builds the cave gaps' test islands from recipes (CICaveGapsBuild [dig|st|uw], default all): cgaps-dig (Dig brush: a tunnel into a hill with a branch and a wide chamber, a second tunnel from beside it dug into the first - a joined system with two mouths - and a tunnel from the sea floor climbing into the land), cgaps-st (cave tool: a cave with a room into one hill, a tunnel through another, open at its far end), cgaps-uw (cave tool: the mouth on the sea floor 5 m down, 12 degrees up to a room above the sea). Saved to Mods; the recipes are kept in recipes/cgaps_*.recipe")]
		public static void CaveGapsBuildCommand(string[] args)
		{
			string which = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "all";
			StartTest(CaveGapsBuildRoutine(which));
		}

		static IEnumerator CaveGapsBuildRoutine(string which)
		{
			bool ok = true;
			foreach (string isl in GapIslands)
			{
				if (which != "all" && isl != "cgaps-" + which) continue;
				bool one = true;
				yield return CaveGapsBuildOne(isl, r => one = r);
				ok &= one;
			}
			if (ok) Log("PASS: cave gaps build"); else Fail("cave gaps build");
		}

		static IEnumerator CaveGapsBuildOne(string isl, Action<bool> result)
		{
			bool ok = true;
			string tag = isl.Substring(6);
			string pa = Path.Combine(RecipeFolder, "cgaps_" + tag + "_a.recipe"), pb = Path.Combine(RecipeFolder, "cgaps_" + tag + ".recipe");
			int seed = tag == "uw" ? 5151 : 4242;
			try { Directory.CreateDirectory(RecipeFolder); File.WriteAllLines(pa, new[] { "# cave gaps test island " + isl + ": the ground", "new", "gen Seed=" + seed + " Radius=130 Height=30 Style=0", "generate" }); }
			catch (Exception e) { Fail("cave gaps build: " + e.Message); result(false); yield break; }
			yield return RecipeRoutine(Path.GetFileNameWithoutExtension(pa));
			yield return new WaitForSecondsRealtime(0.6f);
			Terrain t = terraineditor.terrain;
			if (t == null) { Fail("cave gaps build " + isl + ": no island"); result(false); yield break; }
			float sea = DynamicIslands.EditorSeaInWorld;
			Vector3[] dirs = Enumerable.Range(0, 8).Select(i => Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward).ToArray();
			// (G6: laid with the cave kits of lib_caves.recipe - these islands are the kits' look check)
			Vector3 sysGate = Vector3.zero, sysMouth = Vector3.zero;
			var lines = new List<string> { "# cave gaps test island " + isl + " (CICaveGapsBuild; run after " + Path.GetFileName(pa) + ")", "include lib_caves" };
			Func<Vector3, string> yawOf = d => Num(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
			Func<Vector3, float> ground = p => t.SampleHeight(p) + t.transform.position.y;
			if (tag == "dig" || tag == "st")
			{
				var feet = new List<KeyValuePair<float, Vector3[]>>();
				foreach (Vector3 d in dirs)
				{
					Vector3 m, sd; float sc;
					if (HillFoot(t, d, tag == "st" ? 44f : 10f, out m, out sd, out sc)) feet.Add(new KeyValuePair<float, Vector3[]>(sc, new[] { m, d, sd }));
				}
				feet = feet.OrderByDescending(f => f.Key).ToList();
				if (feet.Count == 0) { Fail("cave gaps build " + isl + ": no hillside"); result(false); yield break; }
				Vector3[] a = feet[0].Value;
				// (the second: a way in at right angles, so the two never cross)
				Vector3[] b = feet.Skip(1).Where(f => Mathf.Abs(Vector3.Dot(f.Value[1], a[1])) < 0.2f).Select(f => f.Value).FirstOrDefault();
				Log("  " + isl + ": hill feet " + string.Join(", ", feet.Take(4).Select(f => f.Value[1].ToString("F1") + " " + (f.Value[0].y - sea).ToString("0.0") + " m up, rock " + f.Key.ToString("0.0")).ToArray()));
				if (tag == "dig")
				{
					Vector3 dir = a[1], side = a[2];
					// (the joined system: a trunk bending to the side, a branch to the other side ending in a chamber, a second mouth
					// beside the first dug in to meet the trunk's end - kit_sys)
					float sgn = Vector3.Dot(side, Vector3.Cross(Vector3.up, dir)) >= 0f ? 1f : -1f;
					Vector3 mb = a[0] + side * 16f;
					mb.y = ground(mb);
					Check(ref ok, Mathf.Abs(mb.y - a[0].y) < 4f, "the second mouth's ground near the first's (" + (mb.y - a[0].y).ToString("0.0") + " m)");
					lines.Add("origin " + XZs(a[0]));
					lines.Add("call kit_sys yaw=" + yawOf(dir) + " side=" + Num(sgn) + " as=gapdig");
					// (cavegate at=: vines across the side branch half way along it, not at the system's first mouth)
					sysGate = dir * 12f - Vector3.Cross(Vector3.up, dir) * sgn * 4f;
					sysMouth = a[0];
					lines.Add("cavegate gapdig at=" + Num(sysGate.x) + "," + Num(sysGate.z) + " as=gapdig_side");
					// (from the sea floor in, rising into the land: a dug tunnel under the sea)
					// (the way in with rock over the whole climb: level under the sea floor to 2 m past the shore, then up at a slope
					// of 12-24 degrees until the floor is 0.8 m over the sea, and 8 m more - the land at least 1 m over the roof all
					// along, so no stretch of it opens to the sky; the way and slope with the most rock over it)
					Vector3 sb = Vector3.zero, sbd = Vector3.zero; float best = 999f, bestFlat = 16f, bestShort = 999f, bestSlope = 14f, bestLen = 30f;
					foreach (Vector3 d in dirs)
					{
						if (Vector3.Dot(d, dir) > 0.5f) continue;
						Vector3 at; float climb;
						if (!Seabed(t, d, 4f, out at, out climb)) continue;
						foreach (float sl in new[] { 12f, 16f, 20f, 24f })
						{
							float tan = Mathf.Tan(sl * Mathf.Deg2Rad), flat = climb + 2f, shortBy = -999f;
							float len = Mathf.Ceil((sea + 0.8f + 1.9f - at.y) / tan) + 8f;
							for (float u = flat; u <= flat + len; u += 1f)
								shortBy = Mathf.Max(shortBy, at.y + (u - flat) * tan + 1.9f + 1f - ground(at + d * u));
							if (shortBy < bestShort - 0.05f) { bestShort = shortBy; best = climb; bestFlat = flat; bestSlope = sl; bestLen = len; sb = at; sbd = d; }
						}
					}
					Check(ref ok, best < 999f, "a sea floor 4 m down by the land (" + (best < 999f ? best.ToString("0") + " m to the shore, " + bestLen.ToString("0") + " m up at " + bestSlope.ToString("0") + " degrees, the land " + (bestShort <= 0f ? "over it all along" : bestShort.ToString("0.0") + " m short somewhere: raised there first") : "none") + ")");
					if (best < 999f)
					{
						lines.Add("origin " + XZs(sb));
						// (land too low over the climb: raised there with the editor's flatten brush - only up, never down - so the
						// test is of the Dig brush, not of this island's shore)
						if (bestShort > 0f)
						{
							float tan = Mathf.Tan(bestSlope * Mathf.Deg2Rad);
							for (float u = bestFlat; u <= bestFlat + bestLen + 4f; u += 4f)
							{
								Vector3 q = sb + sbd * u;
								float need = sb.y + Mathf.Min(u - bestFlat, bestLen) * tan + 1.9f + 2f;
								if (ground(q) >= need) continue;
								lines.Add("brush flatten at " + Num(q.x - sb.x) + " " + Num(q.z - sb.z) + " r=6 s=20 frames=80 h=" + Num(need - DynamicIslands.EditorWaterLevel));
							}
						}
						lines.Add("call kit_dig yaw=" + yawOf(sbd) + " flat=" + Num(bestFlat) + " len=" + Num(bestLen) + " r=1.9 slope=" + Num(bestSlope) + " room=0 titanium=1 as=gapdiguw");
					}
				}
				else
				{
					Check(ref ok, b != null, "two hillsides at right angles");
					lines.Add("origin " + XZs(a[0]));
					lines.Add("call kit_st yaw=" + yawOf(a[1]) + " len=26 end=room light=1 gate=1 as=gapst1");
					if (b != null)
					{
						lines.Add("origin " + XZs(b[0]));
						lines.Add("call kit_st yaw=" + yawOf(b[1]) + " len=32 slope=-4 end=open as=gapst2");
					}
				}
			}
			else
			{
				Vector3 sb = Vector3.zero, sbd = Vector3.zero; float best = 999f;
				foreach (Vector3 d in dirs)
				{
					Vector3 at; float climb;
					if (Seabed(t, d, 5f, out at, out climb) && climb < best) { best = climb; sb = at; sbd = d; }
				}
				if (best >= 999f) { Fail("cave gaps build " + isl + ": no sea floor 5 m down by the land"); result(false); yield break; }
				float depth = sea - ground(sb);
				int pieces = Mathf.Clamp(Mathf.CeilToInt((depth + 2.5f) / (CaveTool.Step * Mathf.Sin(12f * Mathf.Deg2Rad))), 2, 12);
				Log("  " + isl + ": sea floor " + depth.ToString("0.0") + " m down, the shore " + best.ToString("0") + " m in; " + pieces + " pieces up at 12 degrees");
				lines.Add("origin " + XZs(sb));
				lines.Add("call kit_uw yaw=" + yawOf(sbd) + " len=" + Num(pieces * CaveTool.Step + 1f) + " as=gapuw");
				// (caveloot wet=1: loot on the flooded floor too, w03 11-09)
				lines.Add("caveloot gapuw dirt=4 mush=0 stone=0 vines=0 titanium=0 light=0 wet=1");
			}
			lines.Add("save " + isl);
			try { File.WriteAllLines(pb, lines.ToArray()); } catch (Exception e) { Fail("cave gaps build: " + e.Message); result(false); yield break; }
			foreach (string l in lines.Skip(1)) Log("    " + l);
			yield return RecipeRoutine(Path.GetFileNameWithoutExtension(pb));
			yield return new WaitForSecondsRealtime(0.5f);
			Check(ref ok, RecipeSaved == isl, isl + " saved (" + (RecipeSaved ?? "not saved") + ")");
			// The kits' loot: on each cave's dry floor (resting on something, not floating, not sunk in), vines from the roof
			foreach (var kv in recipeGroups.Where(g => g.Key.EndsWith("_loot", StringComparison.OrdinalIgnoreCase)).ToList())
			{
				int resting = 0, vinesN = 0, wetLoot = 0; var bad = new List<string>();
				bool wetOk = kv.Key.Equals("gapuw_loot", StringComparison.OrdinalIgnoreCase);
				foreach (EditorGameObject g in kv.Value.Where(x => x != null))
				{
					Bounds b = ShapeBounds(g.gameObject);
					if (g.GameObjectName.StartsWith("CaveVine")) { vinesN++; RaycastHit? rf = FirstSolid(new Vector3(b.center.x, b.max.y - 0.5f, b.center.z), Vector3.up, 2f); RaycastHit? fl = FirstSolid(new Vector3(b.center.x, b.min.y - 0.05f, b.center.z), Vector3.down, 6f); if (!rf.HasValue || !fl.HasValue || b.min.y - fl.Value.point.y < 1.95f) bad.Add(g.GameObjectName + (rf.HasValue ? "" : " no roof over it") + (fl.HasValue ? " " + (b.min.y - fl.Value.point.y).ToString("0.0") + " m over the floor" : "")); continue; }
					RaycastHit? under = FirstSolid(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z), Vector3.down, 1f);
					float gap = under.HasValue ? b.min.y - under.Value.point.y : 9f;
					if (under.HasValue && gap > -0.35f && gap < 0.15f && under.Value.point.y <= sea + 0.3f && wetOk) { resting++; wetLoot++; }
					else if (under.HasValue && gap > -0.35f && gap < 0.15f && under.Value.point.y > sea + 0.3f) resting++;
					else bad.Add(g.GameObjectName + (under.HasValue ? " " + gap.ToString("0.00") + " m off its floor" + (under.Value.point.y <= sea + 0.3f ? " (wet)" : "") : " no floor under it"));
				}
				Check(ref ok, resting >= 3 && bad.Count == 0, isl + " " + kv.Key + ": " + resting + " loot on the floor, " + vinesN + " vines" + (bad.Count > 0 ? " - " + string.Join(", ", bad.ToArray()) : ""));
				if (wetOk) Check(ref ok, wetLoot >= 1, isl + " " + kv.Key + ": caveloot wet=1 laid " + wetLoot + " on the flooded floor");
			}
			if (tag == "st") Check(ref ok, RecipeObjects.ContainsKey("gapst1_vines"), isl + ": machete vines on gapst1's mouth (cavegate)");
			if (tag == "dig")
			{
				EditorGameObject sv;
				if (!RecipeObjects.TryGetValue("gapdig_side", out sv) || sv == null) Check(ref ok, false, isl + ": machete vines across gapdig's side branch (cavegate at=)");
				else
				{
					Vector3 vp = sv.transform.position, want = sysMouth + sysGate;
					float off = new Vector2(vp.x - want.x, vp.z - want.z).magnitude, fromMouth = new Vector2(vp.x - sysMouth.x, vp.z - sysMouth.z).magnitude;
					RaycastHit? roof = FirstSolid(vp + Vector3.up * 0.5f, Vector3.up, 6f);
					Check(ref ok, off < 2.5f && fromMouth > 8f && roof.HasValue, isl + ": cavegate at= vines " + off.ToString("0.0") + " m from the side branch point, " + fromMouth.ToString("0.0") + " m in from the mouth, " + (roof.HasValue ? "rock over them" : "no roof over them"));
				}
			}
			DigTunnel[] tuns = UnityEngine.Object.FindObjectsOfType<DigTunnel>();
			if (tag == "dig")
			{
				Check(ref ok, tuns.Length >= 3, isl + ": " + tuns.Length + " dug tunnels (" + string.Join("; ", tuns.Select(x => x.Strokes.Count + " strokes, " + x.PointCount + " points").ToArray()) + ")");
				foreach (DigTunnel x in tuns)
				{
					List<Vector3> l0 = DigLine(x, 0);
					Log("  tunnel from " + (l0[0].y - sea).ToString("0.0") + " m (to the sea) to " + string.Join(", ", Enumerable.Range(0, x.Strokes.Count).Select(k => (DigLine(x, k).Last().y - sea).ToString("0.0")).ToArray()) + " m");
				}
				// The join (11-04 tool fix: two dug tunnels cut each other's rock where they meet - before, each closed its lining
				// against the other, and the second tunnel's floor met the first's 1.6 m apart): along the second mouth's tunnel's last
				// 5 m into the first, a body passes and the floor has no step over 0.5 m
				List<EditorGameObject> sysObjs;
				List<DigTunnel> sys = recipeGroups.TryGetValue("gapdig", out sysObjs) ? sysObjs.Where(x => x != null).Select(x => x.GetComponent<DigTunnel>()).Where(x => x != null).Distinct().ToList() : new List<DigTunnel>();
				Check(ref ok, sys.Count == 2, isl + ": the joined system is two dug tunnels (" + sys.Count + ")");
				if (sys.Count == 2)
				{
					Physics.SyncTransforms();
					List<Vector3> lb = DigLine(sys[1], sys[1].Strokes.Count - 1);
					Vector3 j = lb.Last(), pre = lb.LastOrDefault(p => (p - j).magnitude > 5f);
					if (pre == Vector3.zero) pre = lb[0];
					Vector3 jd = (j - pre).normalized;
					float stepMax = 0f, last = float.NaN; int noFloor = 0;
					for (float s = 0f; s <= (j - pre).magnitude + 1.5f; s += 0.5f)
					{
						// (the rock only - not the kit's loot on the floor)
						RaycastHit[] fl = Physics.RaycastAll(pre + jd * s, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore).Where(h => h.collider.GetComponentInParent<DigTunnel>() != null || h.collider is TerrainCollider).OrderBy(h => h.distance).ToArray();
						if (fl.Length == 0) { noFloor++; continue; }
						if (!float.IsNaN(last)) stepMax = Mathf.Max(stepMax, Mathf.Abs(fl[0].point.y - last));
						last = fl[0].point.y;
					}
					// (to the join point and a little on: 1.4 m on is the far end of the first tunnel, a wall that belongs there)
					Vector3 c0 = pre - Vector3.up * 0.3f;
					RaycastHit[] across = Physics.CapsuleCastAll(c0, c0 + Vector3.up * 0.9f, 0.35f, jd, (j - pre).magnitude + 0.3f, ~0, QueryTriggerInteraction.Ignore)
						.Where(h => h.distance > 0f && h.collider.GetComponentInParent<DigTunnel>() != null && Vector3.Angle(h.normal, Vector3.up) > 50f).ToArray();
					Check(ref ok, across.Length == 0 && stepMax < 0.5f && noFloor == 0, isl + ": the second tunnel into the first - " + (across.Length == 0 ? "no rock across the join" : "ROCK across the way " + across.Min(h => h.distance).ToString("0.0") + " m on") + ", floor steps up to " + stepMax.ToString("0.00") + " m" + (noFloor > 0 ? ", " + noFloor + " points with NO floor" : ""));
				}
			}
			if (tag == "uw" && CaveTool.LastLayout != null)
			{
				CaveTool.Layout l = CaveTool.LastLayout;
				Check(ref ok, l.Mouth.y < sea - 3f && l.End.y > sea + 0.5f && l.Room, "the mouth " + (l.Mouth.y - sea).ToString("0.0") + " m (to the sea), the room's floor " + (l.End.y - sea).ToString("0.0") + " m: flooded at the mouth, dry at the room");
			}
			try { File.Copy(pb, Path.Combine(@"D:\Temp\scratch_8e762ddb\cgaps", Path.GetFileName(pb)), true); } catch { }
			result(ok);
		}

		/// <summary>One cave's way: mouth first, floor points, the room's or the far end's last; In: the way in, level.
		/// Open: the first point is a mouth to the outside (a closed start - a dug room's far end - is walked from and back to
		/// itself, 11-08). Shaft: a dug ladder shaft's middle line (foot first) past the walked floor's last point, or null.</summary>
		class GapWay { public string Kind; public List<Vector3> Floor = new List<Vector3>(); public Vector3 In; public bool Open = true; public List<Vector3> Shaft; public float ShaftR; }
		/// <summary>The point s m along a floor line (by its length); false past its end.</summary>
		static bool PointAlong(List<Vector3> f, float s, out Vector3 p)
		{
			p = f.Count > 0 ? f[0] : Vector3.zero;
			for (int i = 1; i < f.Count; i++)
			{
				float d = Vector3.Distance(f[i - 1], f[i]);
				if (s <= d) { p = Vector3.Lerp(f[i - 1], f[i], d > 1e-4f ? s / d : 0f); return true; }
				s -= d;
			}
			return false;
		}

		static Vector3 GapFloor(Vector3 p, float up = 1.2f, float down = 4f)
		{
			foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * up, Vector3.down, up + down, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
			{
				if (h.collider.GetComponentInParent<Network_Player>() != null || h.collider.GetComponentInParent<AI_NetworkBehaviour>() != null) continue;
				return h.point;
			}
			return p;
		}

		/// <summary>Where a dug branch starting at <paramref name="start"/> leaves the main stroke's line: the index of the
		/// main point nearest it, or -1 when it starts more than <paramref name="within"/> m from the main line (a stroke from
		/// its own mouth that meets the passage further in).</summary>
		public static int BranchJoin(List<Vector3> main, Vector3 start, float within)
		{
			if (main == null || main.Count == 0) return -1;
			int near = Enumerable.Range(0, main.Count).OrderBy(i => (main[i] - start).sqrMagnitude).First();
			return (main[near] - start).magnitude <= within ? near : -1;
		}

		/// <summary>Where a dug line turns into a ladder shaft: the index of the point it starts rising (or falling) steeper
		/// than 1.5 m per m across and over 0.3 m between two points, 2 m in all (Rootcore's shaft; a slope=-20 descent or a
		/// 45-degree ramp is walked); -1: none.</summary>
		public static int ShaftStart(List<Vector3> line)
		{
			// (2 m of it at least: a stroke's first point a step under or over the room it leaves - Rootcore's shaft foot and
			// acid cut, 0.7 m - is no shaft)
			Func<int, bool> steep = i =>
			{
				Vector3 d = line[i] - line[i - 1];
				return Mathf.Abs(d.y) > 0.3f && Mathf.Abs(d.y) > 1.5f * new Vector2(d.x, d.z).magnitude;
			};
			for (int i = 1; line != null && i < line.Count; i++)
			{
				if (!steep(i)) continue;
				int j = i;
				while (j + 1 < line.Count && steep(j + 1)) j++;
				if (Mathf.Abs(line[j].y - line[i - 1].y) >= 2f) return i - 1;
				i = j;
			}
			return -1;
		}

		/// <summary>A dug point open to the outside: an opening the stroke cut in the ground within its radius and 2 m (flat),
		/// or the sky straight over it. A room's far end inside the hill is neither (Rootcore: its walk started 3 m "out" of
		/// it in the rock and came back past it into the rock, 11-08).</summary>
		static bool DigOpenAt(DigTunnel t, Vector3 p, float r)
		{
			var flat = new Vector2(p.x, p.z);
			if (t.Openings.Any(o => (o - flat).magnitude <= r + 2f)) return true;
			bool backs = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = false;
			try { return !Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, 60f, ~0, QueryTriggerInteraction.Ignore); }
			finally { Physics.queriesHitBackfaces = backs; }
		}

		/// <summary>Why a ladder shaft from foot to top (heights) can't be climbed with these ladders (each one's bottom and top,
		/// the climb colliders beside the shaft): "" when one ladder or a chain of them (gaps under 1 m) reaches from within
		/// 1.5 m over the foot to within 3 m under the top.</summary>
		public static string ShaftLadderGap(float foot, float top, List<Vector2> spans)
		{
			if (spans == null || spans.Count == 0) return "no ladder in it";
			float reach = foot + 1.5f;
			bool any = false;
			foreach (Vector2 sp in spans.OrderBy(x => x.x))
			{
				if (sp.x > reach) break;
				any = true;
				reach = Mathf.Max(reach, sp.y + 1f);
			}
			if (!any) return "its ladder starts " + (spans.Min(x => x.x) - foot).ToString("0.0") + " m over the foot";
			float got = reach - 1f;
			return got >= top - 3f ? "" : "its ladder ends " + (top - got).ToString("0.0") + " m under the top";
		}

		/// <summary>The air pocket zone (its index) whose sphere holds p; -1: none (CICaveGapsLook's dry end: a den with its own
		/// air zone - Riverfork's - passes on its breath, wet or dry, 11-08).</summary>
		public static int AirCover(Vector3 p, IList<Vector3> centres, IList<float> radii)
		{
			for (int i = 0; i < centres.Count; i++) if ((p - centres[i]).sqrMagnitude <= radii[i] * radii[i]) return i;
			return -1;
		}

		[ConsoleCommand(name: "CIGapShaftUnit", docs: "Dev, anywhere: CICaveGapsLook's dug-way helpers (11-08) - a ladder shaft found in a dug line (not a slope or a ramp), a shaft's ladders that reach or not (none, too short, a chain), the air pocket zone that holds a den's end or none")]
		public static void GapShaftUnitCommand() { StartTest(GapShaftUnitRoutine()); }

		static IEnumerator GapShaftUnitRoutine()
		{
			bool ok = true;
			Func<float, float, int, List<Vector3>> ramp = (dy, dx, n) => Enumerable.Range(0, n).Select(i => new Vector3(i * dx, i * dy, 0f)).ToList();
			Check(ref ok, ShaftStart(ramp(0f, 1.2f, 8)) == -1, "a level passage: no shaft");
			Check(ref ok, ShaftStart(ramp(-0.4f, 1.1f, 8)) == -1, "a slope=-20 descent: no shaft");
			Check(ref ok, ShaftStart(ramp(1f, 1f, 8)) == -1, "a 45-degree ramp: no shaft");
			var room = ramp(0f, 1.2f, 4);
			room.AddRange(Enumerable.Range(1, 6).Select(i => room[3] + new Vector3(0.05f, 1.2f * i, 0f)));
			Check(ref ok, ShaftStart(room) == 3, "a room then a shaft up: it starts at the room's last point (" + ShaftStart(room) + ")");
			Check(ref ok, ShaftStart(ramp(1.17f, 0f, 6)) == 0, "a shaft from its first point (Rootcore's branch 3)");
			var step = ramp(0f, 1.2f, 6); step[0] += Vector3.up * 0.7f;
			Check(ref ok, ShaftStart(step) == -1, "a first point 0.7 m over the rest (Rootcore's shaft foot): no shaft");
			Check(ref ok, ShaftLadderGap(3.2f, 10.2f, new List<Vector2> { new Vector2(3.5f, 9.5f) }) == "", "a 6 m ladder up a 7 m shaft: climbed");
			Check(ref ok, ShaftLadderGap(3.2f, 10.2f, new List<Vector2>()) != "", "no ladder in the shaft: refused (" + ShaftLadderGap(3.2f, 10.2f, new List<Vector2>()) + ")");
			Check(ref ok, ShaftLadderGap(3.2f, 14f, new List<Vector2> { new Vector2(3.5f, 9.5f) }) != "", "a ladder ending 4.5 m under the top: refused");
			Check(ref ok, ShaftLadderGap(3.2f, 10.2f, new List<Vector2> { new Vector2(6f, 9.5f) }) != "", "a ladder starting 2.8 m over the foot: refused");
			Check(ref ok, ShaftLadderGap(0f, 14f, new List<Vector2> { new Vector2(6.5f, 12f), new Vector2(0.5f, 6f) }) == "", "two ladders, one over the other: climbed");
			var cs = new List<Vector3> { new Vector3(40f, 5f, 30f), new Vector3(0f, 0f, 0f) };
			var rs = new List<float> { 6f, 2f };
			Check(ref ok, AirCover(new Vector3(43f, 4.5f, 31f), cs, rs) == 0, "a den's end inside its air zone: covered");
			Check(ref ok, AirCover(new Vector3(52f, 5f, 30f), cs, rs) == -1, "an end 12 m off the air zone: not covered (the dry check stays)");
			Check(ref ok, AirCover(Vector3.one, new List<Vector3>(), new List<float>()) == -1, "no air zones: not covered");
			if (ok) Log("PASS: gap shaft unit"); else Fail("gap shaft unit");
			yield break;
		}

		/// <summary>An island's story gates: shown objects that another object's event or answer hides (hide|name) - bars
		/// over a sea gate opened by an islander's answer, a slab a lever lifts. Not creatures, zones or machete vines (cut
		/// as a player cuts them).</summary>
		static List<IslandObjectRef> StoryGates(IslandWorldState.Entry e)
		{
			List<IslandObjectRef> refs = Behaviours.RefsOf(e).ToList();
			HashSet<string> hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (IslandObjectRef r in refs)
				foreach (KeyValuePair<string, string> kv in r.Props)
				{
					if (!kv.Key.StartsWith(BehaviourProps.EventPrefix) && !kv.Key.StartsWith(BehaviourProps.ElsePrefix)) continue;
					foreach (ObjAction a in ObjAction.ParseLines(kv.Value)) if (a.Verb == "hide" && a.Target.Length > 0 && !a.Target.Equals("self", StringComparison.OrdinalIgnoreCase)) hidden.Add(a.Target);
				}
			return refs.Where(r => r.gameObject.activeInHierarchy && r.Name.Length > 0 && hidden.Contains(r.Name) && r.ObjectName != ContentCatalog.MacheteVines
				&& !ContentCatalog.IsCreature(r.ObjectName) && !ContentCatalog.IsZone(r.ObjectName)).ToList();
		}

		/// <summary>The ways into an island's caves: each cave tool cave from its mouth piece through its tunnels to its room or
		/// far end; each dug tunnel's strokes (a branch reached along the passage it leaves).</summary>
		static List<GapWay> GapWays(IslandWorldState.Entry e)
		{
			var ways = new List<GapWay>();
			Func<Transform, IslandObjectRef> refOf = tr => tr.GetComponent<IslandObjectRef>() ?? tr.GetComponentInParent<IslandObjectRef>();
			Func<Transform, float> sizeOf = tr =>
			{
				IslandObjectRef r = refOf(tr);
				string v = null;
				if (r != null) foreach (string k in new[] { ObjectProps.CaveMouth, ObjectProps.CaveJoin, ObjectProps.CaveEnd }) if (r.Props.TryGetValue(k, out v) && v.Length > 0) break;
				float s;
				return v != null && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s) && s > 0.1f ? s : 1f;
			};
			Func<Transform, float, Vector3> pass = (tr, z) => tr.position + tr.rotation * (new Vector3(CaveTool.PassX, CaveTool.Floor, z) * sizeOf(tr));
			List<Transform> tunnels = e.Root.GetComponentsInChildren<Transform>(true).Where(x => x.name == CaveTool.Tunnel).ToList();
			List<Transform> rooms = e.Root.GetComponentsInChildren<Transform>(true).Where(x => x.name == CaveTool.Room).ToList();
			foreach (Transform first in tunnels.Where(x => { IslandObjectRef r = refOf(x); return r != null && r.Props.ContainsKey(ObjectProps.CaveMouth); }))
			{
				var w = new GapWay { Kind = "cave" };
				Transform cur = first;
				var used = new HashSet<Transform>();
				Vector3 exit = Vector3.zero;
				while (cur != null && used.Add(cur))
				{
					float sz = sizeOf(cur);
					Vector3 a = pass(cur, CaveTool.FaceMin), b = pass(cur, CaveTool.FaceMin + CaveTool.Step);
					foreach (float f in new[] { 0.12f, 0.5f, 0.88f }) w.Floor.Add(GapFloor(Vector3.Lerp(a, b, f)));
					exit = b;
					Vector3 ex = b;
					cur = tunnels.FirstOrDefault(x => !used.Contains(x) && (pass(x, CaveTool.FaceMin) - ex).magnitude < 1.5f * sz);
				}
				w.In = exit - pass(first, CaveTool.FaceMin); w.In.y = 0f; w.In.Normalize();
				Transform room = rooms.Where(x => (x.position - exit).magnitude < 25f).OrderBy(x => (x.position - exit).magnitude).FirstOrDefault();
				if (room != null) { w.Kind = "cave+room"; w.Floor.Add(GapFloor(exit + w.In * 4f)); w.Floor.Add(GapFloor(exit + w.In * 8f)); }
				else { w.Kind = "cave, open end"; w.Floor.Add(GapFloor(exit + w.In * 4f, 2f, 8f)); }
				w.Floor.Insert(0, GapFloor(pass(first, CaveTool.FaceMin) - w.In * 0.5f));
				ways.Add(w);
			}
			foreach (DigTunnel t in e.Root.GetComponentsInChildren<DigTunnel>(true))
			{
				if (t.Strokes == null || t.Strokes.Count == 0) continue;
				List<Vector3> main = DigLine(t, 0);
				for (int k = 0; k < t.Strokes.Count; k++)
				{
					var w = new GapWay { Kind = "dug " + (k == 0 ? "tunnel" : "branch " + k) };
					List<Vector3> line = DigLine(t, k);
					var pts = new List<Vector3>();
					// a branch that leaves the passage: walked in along the main stroke to where it starts; a stroke that starts
					// at its own mouth outside and only meets the passage further in (Old Mine's man-way, round 4) is its own way
					// in - its shots and walk start at that mouth, not at the main stroke's
					int near = k > 0 ? BranchJoin(main, line[0], 3f) : -1;
					if (near >= 0) pts.AddRange(main.Take(near + 1));
					pts.AddRange(line);
					float r = t.Strokes[k].Count > 0 ? t.Strokes[k].Max(p => p.R) : 1.5f;
					w.Open = DigOpenAt(t, pts[0], r);
					// (a stroke dug from inside out: its open end is its mouth)
					if (!w.Open && near < 0 && DigOpenAt(t, pts.Last(), r)) { pts.Reverse(); w.Open = true; }
					// a ladder shaft (11-08, Rootcore's branch 3): walked to its foot, then checked as a ladder's way
					int sh = ShaftStart(pts);
					if (sh == 0 && pts.Count > 1 && pts[0].y > pts.Last().y) { pts.Reverse(); sh = ShaftStart(pts); }
					if (sh >= 0) { w.Shaft = pts.Skip(sh).ToList(); w.ShaftR = r; pts = pts.Take(sh + 1).ToList(); w.Kind += ", ladder shaft"; }
					w.Open = DigOpenAt(t, pts[0], r);
					// every point's floor: a walk between every third cut the passage's bends into the rock (Rootcore, 11-08)
					RaycastHit fh;
					foreach (Vector3 q in pts) w.Floor.Add(DigFloor(q, out fh) ? fh.point : q);
					w.In = pts[Mathf.Min(pts.Count - 1, 6)] - pts[0]; w.In.y = 0f;
					if (w.In.sqrMagnitude < 0.01f && w.Shaft != null) { w.In = w.Shaft.Last() - w.Shaft[0]; w.In.y = 0f; }
					if (w.In.sqrMagnitude < 0.01f) w.In = Vector3.forward;
					w.In.Normalize();
					ways.Add(w);
				}
			}
			return ways;
		}

		[ConsoleCommand(name: "CIGapWaysUnit", docs: "Dev, anywhere: dug-tunnel ways (BranchJoin) - a branch that starts on the passage is walked in along the main stroke; a stroke from its own mouth outside (Old Mine's man-way, round 4) is its own way in, its shots from its own mouth")]
		public static void GapWaysUnitCommand()
		{
			bool ok = true;
			var main = new List<Vector3>();
			for (int i = 0; i <= 10; i++) main.Add(new Vector3(44.7f + i * 0.98f, 4f, -12f + i * 0.3f));
			Check(ref ok, BranchJoin(main, new Vector3(54.5f, 4f, -9f), 3f) == 10, "a branch from the passage's end: joins at its end");
			Check(ref ok, BranchJoin(main, new Vector3(49.6f, 4f, -10.9f), 3f) == 5, "a branch from the passage's middle: joins there");
			Check(ref ok, BranchJoin(main, new Vector3(64.7f, 5f, -19.4f), 3f) == -1, "the man-way from its own mouth 12 m off: its own way in");
			Check(ref ok, BranchJoin(new List<Vector3>(), Vector3.zero, 3f) == -1, "no main line: its own way in");
			if (ok) Log("PASS: gap ways"); else Fail("gap ways");
		}

		[ConsoleCommand(name: "CICaveGapsLook", docs: "Dev, a world (host): one cave gaps test island or any saved island with caves (CICaveGapsLook <cgaps-dig|cgaps-st|cgaps-uw|island name, spaces allowed> [noshots]), the kept one or brought next to the raft: each cave's way in (cave tool tunnels to the room or the far end, each dug stroke) - pictures from outside at the mouth and over the hill, from inside looking in, looking out, at the walls and roof at its end, by day and by night (shot_cg_<island>_<n>_<view>.png); a clear way along it (no rock across it); walked in with Raft's controller and back out past the mouth where it is dry; where it is under the sea: water there (Raft's swimming, the breath going down) and, in a room above the sea, dry ground with the breath coming back")]
		public static void CaveGapsLookCommand(string[] args)
		{
			string name = CaveGapsLookName(args);
			if (name.Length == 0) { Fail("CICaveGapsLook <island>"); return; }
			StartTest(CaveGapsLookRoutine(name, args == null || !args.Contains("noshots")));
		}

		/// <summary>The island's name for CICaveGapsLook: all the words but "noshots", quotes round it dropped - a library
		/// island's name has spaces (Old Mine Islet) and the console splits it into words; only the first word was taken, with
		/// its quote, and the world then tried to read that island ('"Old') every frame (11-04).</summary>
		static string CaveGapsLookName(string[] args)
		{
			if (args == null) return "";
			return string.Join(" ", args.Where(a => !string.Equals(a, "noshots", StringComparison.OrdinalIgnoreCase)).ToArray()).Trim().Trim('"', '\'').Trim();
		}

		[ConsoleCommand(name: "CICaveGapsLookName", docs: "Dev, anywhere: CICaveGapsLook's island name from the console's words - a name with spaces, with or without quotes, with noshots after it; a name with no saved island is refused before anything is brought into the world")]
		public static void CaveGapsLookNameCommand(string[] args)
		{
			var cases = new[]
			{
				new { a = new[] { "Old", "Mine", "Islet" }, w = "Old Mine Islet" },
				new { a = new[] { "\"Old", "Mine", "Islet\"", "noshots" }, w = "Old Mine Islet" },
				new { a = new[] { "cgaps-dig" }, w = "cgaps-dig" },
				new { a = new[] { "noshots" }, w = "" },
			};
			int bad = 0;
			foreach (var c in cases)
			{
				string got = CaveGapsLookName(c.a);
				if (got != c.w) { bad++; Log("  " + string.Join(" ", c.a) + " -> '" + got + "', not '" + c.w + "'"); }
			}
			if (CaveGapsLookIslandMissing("\"Old")) Log("  '\"Old' refused: no saved island"); else { bad++; Log("  '\"Old' not refused"); }
			if (bad == 0) Log("PASS: cave gaps look name: " + cases.Length + " cases, a missing island refused"); else Fail("cave gaps look name: " + bad + " wrong");
		}

		[ConsoleCommand(name: "CICaveStoryGates", docs: "Dev, anywhere: CICaveGapsLook's story gates - the things an object's event or else hides by name (bars a choice opens), opened for the look; not machete vines, animals, zones, self or what is hidden already")]
		public static void CaveStoryGatesCommand(string[] args)
		{
			// (w03 11-09: Thalassa Mooring's sea gate bars, hidden by Elaine's choice 2, failed the look's "a clear way along it")
			var root = new GameObject("ci_storygates");
			try
			{
				Func<string, string, string, IslandObjectRef> make = (obj, name, events) =>
				{
					var go = new GameObject(name);
					go.transform.SetParent(root.transform);
					IslandObjectRef r = go.AddComponent<IslandObjectRef>();
					r.ObjectName = obj;
					r.Props[BehaviourProps.Name] = name;
					if (events != null) foreach (string ev in events.Split(';')) { int i = ev.IndexOf('='); r.Props[ev.Substring(0, i)] = ev.Substring(i + 1); }
					return r;
				};
				make("Islander_Survivor", "keeper", BehaviourProps.EventPrefix + "choice2=hide|bars\nhide|self\nshow|wall;" + BehaviourProps.ElsePrefix + "use=hide|bars2");
				make("Block_Lever", "lever", BehaviourProps.EventPrefix + "use=hide|vines\nhide|rats\nhide|zone\nhide|gone\nhide|nosuch");
				make("TP_Selene_Int_Room_Bars", "bars", null);
				make("TP_Selene_Int_Room_Bars", "bars2", null);
				make("Block_Wall_Tier3", "wall", null);
				make(ContentCatalog.MacheteVines, "vines", null);
				make("Creature_Rat", "rats", null);
				make(ContentCatalog.TriggerZone, "zone", null);
				make("Block_Wall_Tier3", "gone", null).gameObject.SetActive(false);
				var e = new IslandWorldState.Entry { Name = "ci_storygates", HostName = "ci_storygates", Root = root };
				string got = string.Join(",", StoryGates(e).Select(g => g.Name).OrderBy(n => n).ToArray());
				if (got == "bars,bars2") Log("PASS: cave story gates: bars (on.choice2) and bars2 (else.use) opened; vines, rats, zone, self, the hidden and the shown left");
				else Fail("cave story gates: got '" + got + "', not 'bars,bars2'");
			}
			finally { UnityEngine.Object.Destroy(root); }
		}

		/// <summary>The way out of a dug mouth (its first floor point) to walk in from (CICaveGapsLook): straight out (-In) if the ground 3 m out is
		/// within a step of the mouth's floor, else the first of 30, 60, 90 degrees to either side that is - a man-way on a
		/// ledge over a sea slope is come at along the ledge; straight out it was 4 m down the slope and the walk failed
		/// though a player walks in along the ledge (Old Mine Islet, 11-04). None within a step: the one closest in height.</summary>
		static Vector3 GapWalkOut(Vector3 mouth, Vector3 inDir)
		{
			float floor = mouth.y; // (the way's first floor point; a ray down at the mouth can stop on the rim over it)
			var dirs = new List<Vector3>();
			var dys = new List<float>();
			foreach (float a in GapWalkAngles)
			{
				Vector3 d = Quaternion.Euler(0f, a, 0f) * -inDir;
				dirs.Add(d);
				// (the worst of 1, 2 and 3 m out: the walk to the mouth is a straight line, and a start on a ledge's far end with
				// a drop between - the slope under the man-way of Old Mine Islet - let the walker slide off on the way in, 11-05)
				float worst = 0f;
				for (float m = 1f; m <= 3f; m += 1f)
				{
					Vector3 at = mouth + d * m;
					Vector3 g = GapFloor(at, 3f, GapWalkDown);
					float dy = GapWalkDy(g != at, g.y, floor);
					if (Mathf.Abs(dy) > Mathf.Abs(worst)) worst = dy;
				}
				dys.Add(worst);
			}
			return dirs[GapWalkPick(dys)];
		}

		/// <summary>How far down GapWalkOut looks for the ground beside a mouth (from 3 m over the floor).</summary>
		const float GapWalkDown = 30f;

		/// <summary>The ground's height beside a mouth less its floor; no ground under the probe (a cliff, the sea far
		/// below) is a drop past the probe's reach, never level ground.</summary>
		static float GapWalkDy(bool hit, float groundY, float floor) => hit ? groundY - floor : -(GapWalkDown - 3f);

		static readonly float[] GapWalkAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
		const float GapWalkStep = 1.0f;

		/// <summary>Which of GapWalkAngles to walk in from, by the ground's height there less the mouth's floor (in order):
		/// the first within GapWalkStep, else the closest.</summary>
		static int GapWalkPick(IList<float> dys)
		{
			for (int i = 0; i < dys.Count; i++) if (Mathf.Abs(dys[i]) <= GapWalkStep) return i;
			int best = 0;
			for (int i = 1; i < dys.Count; i++) if (Mathf.Abs(dys[i]) < Mathf.Abs(dys[best])) best = i;
			return best;
		}

		[ConsoleCommand(name: "CICaveGapsWalkFrom", docs: "Dev, anywhere: where CICaveGapsLook walks into a dug mouth from - straight out when the ground there is within a step of the floor, else the first side direction that is (a mouth on a ledge), else the closest in height")]
		public static void CaveGapsWalkFromCommand(string[] args) { StartTest(CaveGapsWalkFromRoutine()); }

		/// <summary>(Run as a test so it ends with the IDLE line ci.ps1 -done waits for: as a plain command it printed its
		/// PASS and no done marker, and the run waited out its timeout - 11-05.)</summary>
		static IEnumerator CaveGapsWalkFromRoutine()
		{
			var cases = new[]
			{
				new { d = new[] { 0.2f, 0f, 0f, 0f, 0f, 0f, 0f }, w = 0 },
				new { d = new[] { -4f, -3.5f, -2.5f, -1.5f, -0.8f, 0.1f, -3f }, w = 4 },
				new { d = new[] { -4f, -3f, -3f, -2f, -2f, -0.3f, -0.6f }, w = 5 },
				new { d = new[] { -4f, -3f, -3.5f, -2.2f, -2.5f, -1.8f, -3f }, w = 5 },
				new { d = new[] { 1.0f, -3f, -3f, -3f, -3f, -3f, -3f }, w = 0 },
				// (no ground under the probe straight out or to one side: a drop, so the ledge at -60 is taken, not the miss)
				new { d = new[] { GapWalkDy(false, 0f, 5.7f), GapWalkDy(true, 1.4f, 5.7f), GapWalkDy(false, 0f, 5.7f), GapWalkDy(true, 7.6f, 5.7f), GapWalkDy(true, 5.4f, 5.7f), -3f, -3f }, w = 4 },
			};
			int bad = 0;
			foreach (var c in cases)
			{
				int got = GapWalkPick(c.d);
				if (got != c.w) { bad++; Log("  " + string.Join(" ", c.d.Select(x => x.ToString("0.0")).ToArray()) + " -> " + got + ", not " + c.w); }
			}
			if (bad == 0) Log("PASS: cave gaps walk from: " + cases.Length + " cases (straight out, a ledge to the side, the closest, no ground is a drop)"); else Fail("cave gaps walk from: " + bad + " wrong");
			yield break;
		}

		static bool CaveGapsLookIslandMissing(string name)
		{
			try { return name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || !System.IO.File.Exists(IslandSpawner.PathFor(name)); }
			catch { return true; }
		}

		static IEnumerator CaveGapsLookRoutine(string name, bool shots)
		{
			Network_Player me = RAPI.GetLocalPlayer();
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || me == null) { Fail("cave gaps look: host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			Vector3 home0 = me.transform.position;
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.Root != null && string.Equals(x.HostName, name, StringComparison.OrdinalIgnoreCase));
			if (e == null)
			{
				if (CaveGapsLookIslandMissing(name)) { Fail("cave gaps look: no saved island '" + name + "'"); yield break; }
				// (at its own height in the world, as a world brings it: brought at sea level, a deep island - Lumen Shelf, 25 m
				// down - stood dry and its flooded galleries were walked as dry tunnels, 11-09 w03)
				float elev = 0f, water = 0f;
				try { IslandFile lf = IslandFile.Load(IslandSpawner.PathFor(name)); elev = lf.Elevation; water = lf.WaterLevel; } catch { }
				Vector3? spot = ScSpot(name, 400f, elev);
				if (!spot.HasValue) { Fail("cave gaps look: no open sea near the raft"); yield break; }
				var made = new List<IslandWorldState.Entry>();
				yield return ScBring(name, spot.Value, made);
				e = made.LastOrDefault();
				if (e == null || e.Root == null) { Fail("cave gaps look: " + name + " didn't come"); yield break; }
				// (the root sits the island's water line under the spot: IslandSpawner puts it at elevation - WaterLevel)
				Check(ref ok, Mathf.Abs(e.Root.transform.position.y + water - elev) < 0.5f, name + ": brought at its height in the world (" + elev.ToString("0.#") + " m, its water line at " + (e.Root.transform.position.y + water).ToString("0.#") + ")");
				yield return new WaitForSeconds(4f);
			}
			for (float w = 0f; w < 20f; w += 0.5f)
			{
				CaveMountain m = e.Root.GetComponentInChildren<CaveMountain>();
				if ((m == null || !m.Pending) && e.Root.GetComponentsInChildren<DigTunnel>(true).All(t => t.Triangles > 0 && t.Land() != null)) break;
				yield return new WaitForSeconds(0.5f);
			}
			yield return new WaitForSeconds(1f);
			playEntry = e;
			// (a kit's machete vines over a mouth (cavegate, G6): cut first, as a player with the machete does - CICaveVineGate
			// checks that they close the mouth)
			List<IslandObjectRef> gates = e.Root.GetComponentsInChildren<IslandObjectRef>(true).Where(r => r.ObjectName == ContentCatalog.MacheteVines && r.gameObject.activeInHierarchy && r.Name.Length > 0).ToList();
			if (gates.Count > 0)
			{
				PlayerInventory gi = me.Inventory;
				bool had = gi.GetItemCount(ContentCatalog.MacheteItem) > 0;
				if (!had) gi.AddItem(ContentCatalog.MacheteItem, 1);
				foreach (IslandObjectRef g in gates) ScUse(e, g.Name);
				yield return new WaitForSeconds(0.8f);
				if (!had) gi.RemoveItem(ContentCatalog.MacheteItem, 1);
				Check(ref ok, gates.All(g => !g.gameObject.activeInHierarchy), name + ": the kit's machete vines (" + string.Join(", ", gates.Select(g => g.Name).ToArray()) + ") cut");
				PlayerMove.To(me, home0, ControllerType.Ground);
				yield return new WaitForSeconds(0.5f);
			}
			// (a story gate across a way - bars, a slab - that another object's action hides: Thalassa Mooring's sea gate,
			// opened by Harbourmistress Elaine's answer (w03, 11-09). Opened for the look as the story opens it, put back after)
			List<IslandObjectRef> storyGates = StoryGates(e);
			foreach (IslandObjectRef g in storyGates) g.gameObject.SetActive(false);
			if (storyGates.Count > 0) Log("  " + name + ": story gates opened for the look (as their story opens them): " + string.Join(", ", storyGates.Select(g => g.Name).Distinct().ToArray()));
			Physics.SyncTransforms();
			const float sea = 0f;
			List<GapWay> ways = GapWays(e);
			Check(ref ok, ways.Count > 0, name + ": " + ways.Count + " ways in (" + string.Join(", ", ways.Select(w => w.Kind + " " + w.Floor.Count + " points, " + (w.Floor[0].y - sea).ToString("0.0") + " to " + (w.Floor.Last().y - sea).ToString("0.0") + " m").ToArray()) + ")");
			// The dug mouths' rims: none of the rock's faces lying on the ground beside them (pale saw-tooth shards where the rim's
			// skin crosses a steep slope - Old Mine Islet's shore adit, 11-05)
			foreach (DigTunnel dt in e.Root.GetComponentsInChildren<DigTunnel>(true))
			{
				if (dt.RimColumns == 0) continue;
				string at = "";
				int shards = dt.RimTop == "" ? 0 : dt.RimShards(out at);
				Check(ref ok, shards == 0, name + ": dug tunnel " + dt.name + "'s rim (" + dt.RimColumns + " columns, its top " + (dt.RimTop == "" ? "rock" : "the ground's") + "): " + shards + " rock shards on the ground" + (shards > 0 ? " at " + at : ""));
			}
			Vector3 eyeUp = Vector3.up * 1.6f;
			Vector3 home = me.transform.position;
			int n = 0;
			try
			{
				foreach (GapWay w in ways)
				{
					n++;
					string label = name + " " + n + " (" + w.Kind + ")";
					string shot = "cg_" + name.Replace("cgaps-", "") + "_" + n + "_";
					List<Vector3> f = w.Floor;
					Vector3 mouth = f[0], end = f.Last();
					int c = f.Count;
					bool wet = mouth.y < sea - 0.5f;
					// A clear way: nothing solid across the passage between its points (a body 0.35 m round, knee to head)
					int blocked = 0;
					string where = "";
					for (int i = 1; i < c; i++)
					{
						Vector3 a = f[i - 1] + Vector3.up * 0.7f, b = f[i] + Vector3.up * 0.7f;
						Vector3 d = b - a;
						if (d.magnitude < 0.2f) continue;
						RaycastHit h;
						if (Physics.CapsuleCast(a, a + Vector3.up * 0.9f, 0.35f, d.normalized, out h, d.magnitude, ~0, QueryTriggerInteraction.Ignore)
							&& h.collider.GetComponentInParent<Network_Player>() == null && h.collider.GetComponentInParent<AI_NetworkBehaviour>() == null
							&& Vector3.Angle(h.normal, Vector3.up) > 50f)
						{ blocked++; if (where.Length == 0) where = " - first at point " + i + " (" + h.collider.name + ", " + (f[i].y - sea).ToString("0.0") + " m)"; }
					}
					Check(ref ok, blocked == 0, label + ": a clear way along it, " + c + " points" + (blocked > 0 ? ": " + blocked + " blocked" + where : ""));
					// A ladder shaft (11-08): its middle line clear (the ladders aside) and ladders up it from its foot to near its top
					if (w.Shaft != null && w.Shaft.Count > 1)
					{
						Func<Collider, bool> ladderish = col => col.name.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0
							|| ((col.GetComponentInParent<IslandObjectRef>()?.ObjectName ?? "").IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0);
						int sb = 0; string sAt = "";
						for (int i = 1; i < w.Shaft.Count; i++)
						{
							Vector3 a = w.Shaft[i - 1], d = w.Shaft[i] - a;
							if (d.magnitude < 0.1f) continue;
							foreach (RaycastHit h in Physics.SphereCastAll(a, 0.3f, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
							{
								if (h.distance <= 0f || ladderish(h.collider) || h.collider.GetComponentInParent<Network_Player>() != null || h.collider.GetComponentInParent<AI_NetworkBehaviour>() != null) continue;
								sb++; if (sAt.Length == 0) sAt = " - first " + h.collider.name + " at " + (h.point.y - sea).ToString("0.0") + " m";
								break;
							}
						}
						float foot = w.Shaft[0].y, top = w.Shaft.Last().y;
						var spans = new List<Vector2>();
						foreach (Collider col in e.Root.GetComponentsInChildren<Collider>(false).Where(x => x.enabled && x.name.IndexOf("climb", StringComparison.OrdinalIgnoreCase) >= 0))
						{
							Bounds lb = col.bounds;
							bool beside = w.Shaft.Any(q => new Vector2(lb.ClosestPoint(q).x - q.x, lb.ClosestPoint(q).z - q.z).magnitude <= w.ShaftR + 0.5f && q.y >= lb.min.y - 2f && q.y <= lb.max.y + 2f);
							if (beside) spans.Add(new Vector2(lb.min.y, lb.max.y));
						}
						string gap = ShaftLadderGap(foot, top, spans);
						Check(ref ok, sb == 0 && gap == "", label + ": a ladder's way up the shaft, " + (foot - sea).ToString("0.0") + " to " + (top - sea).ToString("0.0") + " m (ladders " + string.Join(", ", spans.Select(x => (x.x - sea).ToString("0.0") + "-" + (x.y - sea).ToString("0.0")).ToArray()) + ")" + (sb > 0 ? ": " + sb + " blocked" + sAt : "") + (gap.Length > 0 ? ": " + gap : ""));
					}
					// A whole roof over the way in: rays straight down from 40 m over it, from 1 m to 6 m in and across the passage
					// every 0.2 m, all stop on the rock over it, none reaches the floor (at the mouth's frame a flare once reached
					// through the top - small holes seen from above, 11-04; down, as rays up miss the ground's back faces; cave tool
					// ways only: a dug stroke's mouth is open to the sky for metres by design, a cut ramp down into the hill)
					int rays = 0, sky = 0;
					string skyAt = "";
					Vector3 across = Vector3.Cross(Vector3.up, w.In).normalized;
					for (float s = 1f; s <= 6f && !w.Kind.StartsWith("dug"); s += 0.25f)
					{
						Vector3 p;
						if (!PointAlong(f, s, out p)) break;
						for (float x = -0.6f; x <= 0.61f; x += 0.2f)
						{
							rays++;
							RaycastHit top;
							if (!Physics.Raycast(p + across * x + Vector3.up * 40f, Vector3.down, out top, 41f, ~0, QueryTriggerInteraction.Ignore)
								|| top.point.y < p.y + 1.3f)
							{ sky++; if (skyAt.Length == 0) skyAt = " - first " + s.ToString("0.0") + " m in, " + x.ToString("0.0") + " across"; }
						}
					}
					if (rays > 0) Check(ref ok, sky == 0, label + ": a whole roof over the way in, " + rays + " rays down" + (sky > 0 ? ": " + sky + " reach the floor" + skyAt : ""));

					if (shots)
					{
						Vector3 outside = mouth - w.In * (wet ? 7f : 10f) + Vector3.up * (wet ? 1.2f : 2.5f);
						foreach (bool night in new[] { false, true })
						{
							SetHour(night ? 23f : 12f);
							yield return new WaitForSeconds(0.6f);
							string tod = night ? "night_" : "";
							yield return CameraShot(outside, mouth + Vector3.up * 1.5f, shot + tod + "out");
							if (!night) yield return CameraShot(mouth - w.In * 2f + Vector3.up * 26f, mouth + w.In * 12f, shot + "above");
							yield return CameraShot(f[c / 3] + eyeUp, f[Mathf.Min(c - 1, 2 * c / 3 + 1)] + eyeUp, shot + tod + "in");
							yield return CameraShot(f[Mathf.Max(1, 2 * c / 3)] + eyeUp, mouth + eyeUp, shot + tod + "lookout");
							Vector3 dEnd = end - f[Mathf.Max(0, c - 3)]; dEnd.y = 0f;
							if (dEnd.sqrMagnitude < 0.01f) dEnd = w.In;
							yield return CameraShot(f[Mathf.Max(0, c - 2)] + eyeUp, end + dEnd.normalized * 3f + Vector3.up * 4.5f, shot + tod + "endroof");
						}
						SetHour(12f);
					}

					// Water where it is under the sea; dry ground above it
					PersonController pc = me.PersonController;
					Stat_Oxygen air = me.Stats.stat_oxygen;
					Vector3? deep = f.Where(p => p.y < sea - 2.5f).OrderBy(p => p.y).Select(p => (Vector3?)p).FirstOrDefault();
					if (deep.HasValue)
					{
						Vector3 at = deep.Value + Vector3.up * 0.9f;
						PlayerMove.To(me, at, ControllerType.Ground);
						KeepAlive(me);
						yield return new WaitForSeconds(1.5f);
						bool swims = pc.controllerType == ControllerType.Water;
						air.Value = air.Max;
						float a0 = air.Value;
						for (float s = 0f; s < 3f; s += Time.deltaTime)
						{
							if ((me.transform.position - at).magnitude > 1f) PlayerMove.To(me, at, ControllerType.Water);
							yield return null;
						}
						Check(ref ok, swims && air.Value < a0 - 0.01f, label + ": " + (sea - deep.Value.y).ToString("0.0") + " m under the sea it is water (" + (swims ? "swimming" : pc.controllerType.ToString()) + ", breath " + a0.ToString("0") + " -> " + air.Value.ToString("0.0") + ")");
						if (shots) yield return CameraShot(deep.Value + Vector3.up * 1.4f - w.In * 0.5f, deep.Value + Vector3.up * 1.4f + w.In * 6f, shot + "underwater");
					}
					if (wet && end.y > sea + 0.3f)
					{
						// (KeepAlive fills the breath: first it, then the breath set low)
						Vector3 dryAt = end;
						PlayerMove.To(me, dryAt + Vector3.up * 0.3f, ControllerType.Ground);
						KeepAlive(me);
						yield return new WaitForSeconds(1.5f);
						Vector3 p1 = me.transform.position;
						ControllerType c1 = pc.controllerType;
						air.Value = air.Max * 0.3f;
						float a0 = air.Value;
						yield return new WaitForSeconds(3f);
						Vector3 p2 = me.transform.position;
						Log("  " + label + ": at the end " + (p1.y - sea).ToString("0.0") + " m over the sea (" + c1 + ") after 1.5 s, " + (p2.y - sea).ToString("0.0") + " m (" + pc.controllerType + ") 3 s later, " + new Vector2(p2.x - dryAt.x, p2.z - dryAt.z).magnitude.ToString("0.0") + " m from where it was put");
						if (pc.controllerType != ControllerType.Ground)
						{
							// (what is under the end: every collider on a ray down from 3 m over it, and the ground's height there)
							string under = string.Join(", ", Physics.RaycastAll(dryAt + Vector3.up * 3f, Vector3.down, 15f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Select(h => h.collider.name + " " + (h.point.y - sea).ToString("0.0")).ToArray());
							Terrain land = Terrain.activeTerrains.FirstOrDefault(t2 => { Vector3 o = t2.transform.position, z = t2.terrainData.size; return dryAt.x >= o.x && dryAt.x <= o.x + z.x && dryAt.z >= o.z && dryAt.z <= o.z + z.z; });
							Log("  " + label + ": under the end: " + (under.Length > 0 ? under : "nothing") + "; the ground there " + (land != null ? (land.SampleHeight(dryAt) + land.transform.position.y - sea).ToString("0.0") + " m" : "none"));
						}
						// (a den with its own air pocket zone over its end - Riverfork's - is planned air: its breath comes back wet
						// or dry, 11-08; with none it must be dry ground)
						List<TriggerZone> airs = e.Root.GetComponentsInChildren<TriggerZone>(false).Where(z => z.Air).ToList();
						int pocket = AirCover(dryAt, airs.Select(z => z.transform.position).ToList(), airs.Select(z => z.Radius).ToList());
						if (pocket >= 0 && pc.controllerType != ControllerType.Ground)
						{
							// (11-09 w03, Thalassa's sea gate: put on the line's end - a cave piece's floor, 0.9 m up - the player fell
							// through it into the flooded tunnel under it and the breath went down. A walker comes up the floor into the
							// room (its play does): the breath is checked again standing on the floor under the pocket's middle)
							Vector3 zc = airs[pocket].transform.position;
							RaycastHit fl;
							if (Physics.Raycast(zc + Vector3.up * 0.5f, Vector3.down, out fl, 12f, ~0, QueryTriggerInteraction.Ignore) && fl.point.y > sea)
							{
								PlayerMove.To(me, fl.point + Vector3.up * 0.3f, ControllerType.Ground);
								KeepAlive(me);
								yield return new WaitForSeconds(1.5f);
								air.Value = air.Max * 0.3f;
								a0 = air.Value;
								yield return new WaitForSeconds(3f);
								Log("  " + label + ": on the floor under the pocket's middle, " + (fl.point.y - sea).ToString("0.0") + " m over the sea (" + fl.collider.name + "): " + pc.controllerType + ", " + (me.transform.position.y - sea).ToString("0.0") + " m 3 s later");
							}
						}
						if (pocket >= 0)
							Check(ref ok, air.Value > a0 + 1f, label + ": the end above the sea (" + (dryAt.y - sea).ToString("0.0") + " m) is a planned air pocket '" + airs[pocket].Id + "', the breath comes back (" + pc.controllerType + ", " + a0.ToString("0") + " -> " + air.Value.ToString("0") + ")");
						else
						Check(ref ok, pc.controllerType == ControllerType.Ground && air.Value > a0 + 1f, label + ": the end above the sea (" + (dryAt.y - sea).ToString("0.0") + " m) is dry, the breath comes back (" + pc.controllerType + ", " + a0.ToString("0") + " -> " + air.Value.ToString("0") + ")");
						air.Value = air.Max;
					}
					// Walked in with Raft's controller and back out past the mouth (where it is dry the whole way)
					if (c < 2) Log("  " + label + ": its foot only, not walked");
					else if (f.All(p => p.y > sea + 0.2f))
					{
						// (from the side when the ground straight out falls away: a mouth on a ledge is come at along the ledge; a
						// closed start - a room's far end in the hill - from itself, and back to it, 11-08)
						Vector3 outDir = w.Open ? GapWalkOut(mouth, w.In) : -w.In;
						Vector3 from = w.Open ? GapFloor(mouth + outDir * 3f, 3f, GapWalkDown) : mouth;
						Log("  " + label + ": walks in from " + from.ToString("F1") + " (world), the mouth " + mouth.ToString("F1") + ", out " + outDir.ToString("F2"));
						PlayerMove.To(me, from + Vector3.up * 0.3f, ControllerType.Ground);
						yield return new WaitForSeconds(0.8f);
						bool inOk = false, outOk = false; string why = "", why2 = "";
						yield return PlayWalk(me, new List<Vector3>(f), (r, m) => { inOk = r; why = m; });
						if (inOk)
						{
							var back = new List<Vector3>(f); back.Reverse();
							if (w.Open) back.Add(GapFloor(mouth + outDir * 5f, 3f, GapWalkDown));
							yield return PlayWalk(me, back, (r, m) => { outOk = r; why2 = m; });
						}
						Check(ref ok, inOk && outOk, label + ": walked in to its end" + (inOk ? (w.Open ? " and back out past the mouth" : " and back to its start (a closed end)") + (outOk ? "" : " - NOT: " + why2) : " - NOT: " + why));
					}
					else if (!wet) Log("  " + label + ": dips under the sea on the way (" + (f.Min(p => p.y) - sea).ToString("0.0") + " m): not walked");
				}
			}
			finally
			{
				SetHour(12f);
				try { PlayerMove.To(me, home, ControllerType.Ground); } catch { }
				foreach (IslandObjectRef g in storyGates) if (g != null) g.gameObject.SetActive(true);
			}
			if (ok) Log("PASS: cave gaps look " + name); else Fail("cave gaps look " + name);
		}
	

		// G5: Raft's machete vines over a cave tool mouth close it until they are cut (no code of their own: their colliders)
		[ConsoleCommand(name: "CICaveVineGate", docs: "Dev, a world (host): machete vines over a cave's mouth - cgaps-st (CICaveGapsBuild st) saved again as cgaps-vine with Raft's ChoppableVines on its first cave's mouth, brought next to the raft: a body's width finds no way past them anywhere across the mouth, the player walking in is stopped outside, they stay without a machete, the machete cuts them, then the way is clear and the player walks in and back out (pictures shot_cg_vine_before/after.png). CICaveVineGate [yaw] turns the vines that many more degrees")]
		public static void CaveVineGateCommand(string[] args)
		{
			float yaw = 0f;
			if (args != null && args.Length > 0) float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out yaw);
			StartTest(CaveVineGateRoutine(yaw));
		}

		const string VineGateIsland = "cgaps-vine";

		/// <summary>Ways past the vines at the mouth: a body (0.3 m round, knee to head) sent in from 2 m outside at each step
		/// across the mouth; those that reach 2 m inside. Also how wide the mouth is (its walls at chest height).</summary>
		static List<float> VineGaps(Vector3 mouth, Vector3 inDir, Transform vines, out float width, out int byVines)
		{
			Vector3 side = Vector3.Cross(Vector3.up, inDir).normalized;
			Vector3 chest = mouth + inDir * 0.8f + Vector3.up * 1.2f;
			Func<Vector3, float> wall = d =>
			{
				foreach (RaycastHit h in Physics.RaycastAll(chest, d, 6f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
					if (vines == null || !h.collider.transform.IsChildOf(vines)) if (h.collider.GetComponentInParent<Network_Player>() == null) return h.distance;
				return 6f;
			};
			float l = wall(-side), r = wall(side);
			width = l + r;
			var gaps = new List<float>();
			byVines = 0;
			for (float o = -l + 0.3f; o <= r - 0.3f + 0.001f; o += 0.25f)
			{
				Vector3 a = mouth - inDir * 2f + side * o + Vector3.up * 0.45f;
				RaycastHit[] hits = Physics.CapsuleCastAll(a, a + Vector3.up * 1.0f, 0.3f, inDir, 4f, ~0, QueryTriggerInteraction.Ignore)
					.Where(h => h.collider.GetComponentInParent<Network_Player>() == null && h.collider.GetComponentInParent<AI_NetworkBehaviour>() == null && h.distance > 0f)
					.OrderBy(h => h.distance).ToArray();
				bool vine = vines != null && hits.Any(h => h.collider.transform.IsChildOf(vines));
				// (a rock hit with its face turned up is the floor rising: not a block)
				bool rock = hits.Any(h => (vines == null || !h.collider.transform.IsChildOf(vines)) && Vector3.Angle(h.normal, Vector3.up) > 50f);
				if (vine) byVines++;
				if (!vine && !rock) gaps.Add(o);
			}
			return gaps;
		}

		static IEnumerator CaveVineGateRoutine(float yawMore)
		{
			Network_Player me = RAPI.GetLocalPlayer();
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || me == null) { Fail("cave vine gate: host, in a world"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			string src = IslandSpawner.PathFor("cgaps-st");
			if (!File.Exists(src)) { Fail("cave vine gate: no cgaps-st (CICaveGapsBuild st in the editor first)"); yield break; }
			IslandFile f = IslandFile.Load(src);
			IslandObject m = f.Objects.FirstOrDefault(o => o.Name == CaveTool.Tunnel && o.Props != null && o.Props.ContainsKey(ObjectProps.CaveMouth));
			if (m == null) { Fail("cave vine gate: cgaps-st has no cave mouth"); yield break; }
			float size;
			if (!float.TryParse(ObjectProps.Get(m.Props, ObjectProps.CaveMouth), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size) || size < 0.1f) size = m.Scale.x;
			Quaternion rot = Quaternion.Euler(m.EulerRotation);
			Vector3 at = m.Position + rot * (new Vector3(CaveTool.PassX, CaveTool.Floor, CaveTool.FaceMin) * size);
			Vector3 inDir = rot * Vector3.forward; inDir.y = 0f; inDir.Normalize();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { ContentCatalog.MacheteVines });
			// (the vines the cave kit laid on the mouth (cavegate, G6) when there are some; else, or turned, put there here)
			IslandObject kitVines = f.Objects.FirstOrDefault(o => o.Name == ContentCatalog.MacheteVines && o.Props != null && ObjectProps.Get(o.Props, BehaviourProps.Name).Length > 0);
			string vname = "gatevines";
			if (kitVines != null && Mathf.Abs(yawMore) < 0.01f)
			{
				vname = ObjectProps.Get(kitVines.Props, BehaviourProps.Name);
				Vector3 kin = Quaternion.Euler(kitVines.EulerRotation) * Vector3.forward; kin.y = 0f; kin.Normalize();
				Log("  the kit's vines '" + vname + "', " + (kitVines.Position - (at + inDir * 0.3f)).magnitude.ToString("0.00") + " m from the mouth's middle, turned " + Vector3.Angle(kin, inDir).ToString("0") + " degrees off the way in");
			}
			else
			{
				Dictionary<string, string> vp = ObjectProps.Defaults(ContentCatalog.MacheteVines);
				vp[BehaviourProps.Name] = vname;
				f.Objects.RemoveAll(o => o.Name == ContentCatalog.MacheteVines);
				f.Objects.Add(new IslandObject { Name = ContentCatalog.MacheteVines, Position = at + inDir * 0.3f, EulerRotation = new Vector3(0f, Quaternion.LookRotation(inDir).eulerAngles.y + yawMore, 0f), Props = vp });
			}
			IslandWorldState.Remove(VineGateIsland);
			f.Save(IslandSpawner.PathFor(VineGateIsland));
			Vector3? spot = ScSpot(VineGateIsland, 400f);
			if (!spot.HasValue) { Fail("cave vine gate: no open sea near the raft"); yield break; }
			var made = new List<IslandWorldState.Entry>();
			yield return ScBring(VineGateIsland, spot.Value, made);
			IslandWorldState.Entry e = made.LastOrDefault();
			if (e == null || e.Root == null) { Fail("cave vine gate: " + VineGateIsland + " didn't come"); yield break; }
			for (float w = 0f; w < 20f; w += 0.5f)
			{
				CaveMountain cm = e.Root.GetComponentInChildren<CaveMountain>();
				if (cm == null || !cm.Pending) break;
				yield return new WaitForSeconds(0.5f);
			}
			yield return new WaitForSeconds(3f);
			Physics.SyncTransforms();
			playEntry = e;
			Vector3 home = me.transform.position;
			try
			{
				IslandObjectRef vines = ScObjOf(e, vname);
				GapWay way = GapWays(e).FirstOrDefault(x => x.Kind.StartsWith("cave") && (x.Floor[0] - (e.Root.transform.TransformPoint(at))).magnitude < 3f) ?? GapWays(e).FirstOrDefault(x => x.Kind.StartsWith("cave"));
				Check(ref ok, vines != null && way != null, "the vines and the cave are on the island");
				if (vines == null || way == null) yield break;
				Vector3 mouth = way.Floor[0];
				Collider[] cols = vines.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.enabled).ToArray();
				Bounds b = cols.Length > 0 ? cols[0].bounds : new Bounds(vines.transform.position, Vector3.zero);
				foreach (Collider c in cols) b.Encapsulate(c.bounds);
				Vector3 side = Vector3.Cross(Vector3.up, way.In);
				Log("  the vines: " + cols.Length + " solid colliders (" + string.Join(", ", cols.Select(c => c.GetType().Name + " " + LayerMask.LayerToName(c.gameObject.layer)).Distinct().ToArray()) + "), " + Mathf.Abs(Vector3.Dot(b.size, side)).ToString("0.0") + " m across the way, " + b.size.y.ToString("0.0") + " m high, from " + (b.min.y - mouth.y).ToString("0.0") + " m over the floor");
				float width; int byVines;
				List<float> gaps = VineGaps(mouth, way.In, vines.transform, out width, out byVines);
				Check(ref ok, gaps.Count == 0 && byVines > 0, "the mouth (" + width.ToString("0.0") + " m wide) closed by the vines: " + byVines + " body-wide ways in stopped by them, " + (gaps.Count == 0 ? "none open" : gaps.Count + " open at " + string.Join(", ", gaps.Select(g => g.ToString("0.0")).ToArray()) + " m from the middle"));
				yield return CameraShot(mouth - way.In * 7f + Vector3.up * 2f, mouth + Vector3.up * 1.4f, "cg_vine_before");

				// Walked at: stopped outside
				PlayerMove.To(me, GapFloor(mouth - way.In * 4f, 3f, 6f) + Vector3.up * 0.3f, ControllerType.Ground);
				yield return new WaitForSeconds(0.8f);
				bool walked = false; string why = "";
				yield return PlayWalk(me, way.Floor.Take(Mathf.Min(way.Floor.Count, 5)).ToList(), (r, s) => { walked = r; why = s; });
				float inside = Vector3.Dot(me.transform.position - mouth, way.In);
				Check(ref ok, !walked && inside < 1f, "walking in is stopped at the vines (" + (walked ? "got in" : why) + ", " + inside.ToString("0.0") + " m past the mouth)");

				// Not without a machete; with it they go
				PlayerInventory inv = me.Inventory;
				int had = inv.GetItemCount(ContentCatalog.MacheteItem);
				inv.RemoveItem(ContentCatalog.MacheteItem, 99);
				ScUse(e, vname);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, vines.gameObject.activeInHierarchy, "without a machete the vines stay");
				inv.AddItem(ContentCatalog.MacheteItem, 1);
				ScUse(e, vname);
				yield return new WaitForSeconds(0.8f);
				Check(ref ok, !vines.gameObject.activeInHierarchy, "the machete cuts them");
				inv.RemoveItem(ContentCatalog.MacheteItem, 99);
				if (had > 0) inv.AddItem(ContentCatalog.MacheteItem, had);
				Physics.SyncTransforms();
				gaps = VineGaps(mouth, way.In, null, out width, out byVines);
				Check(ref ok, gaps.Count > 0, "then the way is open (" + gaps.Count + " body-wide ways in)");
				yield return CameraShot(mouth - way.In * 7f + Vector3.up * 2f, mouth + Vector3.up * 1.4f, "cg_vine_after");
				PlayerMove.To(me, GapFloor(mouth - way.In * 4f, 3f, 6f) + Vector3.up * 0.3f, ControllerType.Ground);
				yield return new WaitForSeconds(0.8f);
				bool inOk = false, outOk = false; string w1 = "", w2 = "";
				yield return PlayWalk(me, new List<Vector3>(way.Floor), (r, s) => { inOk = r; w1 = s; });
				if (inOk)
				{
					var back = new List<Vector3>(way.Floor); back.Reverse();
					back.Add(GapFloor(mouth - way.In * 5f, 3f, 6f));
					yield return PlayWalk(me, back, (r, s) => { outOk = r; w2 = s; });
				}
				Check(ref ok, inOk && outOk, "after the cut the player walks in and back out" + (inOk ? (outOk ? "" : " - NOT out: " + w2) : " - NOT in: " + w1));
			}
			finally
			{
				try { PlayerMove.To(me, home, ControllerType.Ground); } catch { }
			}
			if (ok) Log("PASS: cave vine gate"); else Fail("cave vine gate");
		}

		// G6 cave kits (11-04): the recipe steps caveloot and cavegate (lib_caves.recipe's kits use them)
		static readonly List<string> CaveKitObjects = new List<string> { "Pickup_Landmark_DirtPickup", "Pickup_Landmark_Mushroom 1", "Pickup_Landmark_Mushroom 2",
			"Pickup_Landmark_Mushroom 3", "Pickup_Landmark_Rock_2_Land", "Pickup_Landmark_Titanium_Land", "CaveVine1", "CaveVine2", "CaveVine3",
			ContentCatalog.MacheteVines, "Placeable_Lantern_Metal" };

		/// <summary>The middle line of a cave laid as= group (cave tool or Dig brush), about 1 m apart, at chest height (world).</summary>
		static List<List<Vector3>> CaveLines(string group)
		{
			var lines = new List<List<Vector3>>();
			CaveTool.Layout l;
			if (recipeCaves.TryGetValue(group, out l) && l != null)
			{
				var line = new List<Vector3>();
				Vector3 flat = new Vector3(l.Flat.x, 0f, l.Flat.z).normalized;
				for (float s = 0f; s <= l.Length + 0.01f; s += 1f) line.Add(new Vector3(l.Mouth.x, 0f, l.Mouth.z) + flat * s + Vector3.up * (l.FloorAt(s) + 1.2f));
				// (the den or the open end beyond: as far as the floor goes on - CaveLootStep keeps only the spots inside rock)
				Vector3 last = line[line.Count - 1];
				for (float s = 1f; s <= 9f; s += 1f) line.Add(last + flat * s);
				lines.Add(line);
				// (11-09 w03: a group of cave pieces and dug strokes - Lumen Shelf's and Stalker's Hollow's galleries dug under
				// their open cave pieces, shafts between - kept only the last cave piece's line, every point of it near the
				// open sky, and caveloot found no spot: the dug strokes of the group count too)
			}
			List<EditorGameObject> objs;
			if (!recipeGroups.TryGetValue(group, out objs)) return lines;
			foreach (EditorGameObject e in objs.Distinct())
			{
				DigTunnel t = e != null ? e.GetComponent<DigTunnel>() : null;
				if (t == null) continue;
				foreach (List<DigTunnel.Point> stroke in t.Strokes)
				{
					var line = new List<Vector3>();
					for (int i = 0; i < stroke.Count; i++)
					{
						Vector3 a = t.transform.TransformPoint(stroke[i].At) + Vector3.up * 1.2f;
						if (i == 0) { line.Add(a); continue; }
						Vector3 b = line[line.Count - 1];
						int n = Mathf.Max(1, Mathf.RoundToInt((a - b).magnitude));
						for (int k = 1; k <= n; k++) line.Add(Vector3.Lerp(b, a, (float)k / n));
					}
					if (line.Count > 0) lines.Add(line);
				}
			}
			return lines;
		}

		static RaycastHit? FirstSolid(Vector3 from, Vector3 dir, float range)
		{
			RaycastHit[] hits = Physics.RaycastAll(from, dir, range, ~0, QueryTriggerInteraction.Ignore).Where(h => h.collider.GetComponentInParent<Network_Player>() == null).OrderBy(h => h.distance).ToArray();
			if (hits.Length == 0) return null;
			return hits[0];
		}

		/// <summary>A world point in the recipe's current frame (x across, z ahead).</summary>
		static Vector2 FrameXZ(Vector3 w)
		{
			Frame f = TopFrame;
			Vector3 v = Quaternion.Euler(0f, -f.Yaw, 0f) * new Vector3(w.x - f.Origin.x, 0f, w.z - f.Origin.y);
			return new Vector2(v.x, v.z);
		}

		static Bounds ShapeBounds(GameObject go)
		{
			Renderer[] rs = PlacementOptions.ShapeRenderers(go);
			if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			return b;
		}

		static void AddToGroup(string group, EditorGameObject e)
		{
			List<EditorGameObject> g;
			if (!recipeGroups.TryGetValue(group, out g)) recipeGroups[group] = g = new List<EditorGameObject>();
			g.Add(e);
		}

		/// <summary>
		/// caveloot: Raft's cave loot (dirt, mushrooms, stone, titanium, a lantern) on the floor inside the cave laid as=group, spread
		/// along it from 5 m in (past the daylight at the mouth) to its end, to the passage's sides in turn; cave vines hung from its
		/// roof, short enough to walk under. Wet floor (under the sea's level + 0.3 m) gets none. Laid as=group_loot.
		/// </summary>
		static string CaveLootStep(string group, Dictionary<string, string> opt, ref int placed)
		{
			List<List<Vector3>> lines = CaveLines(group);
			if (lines.Count == 0) return "caveloot: no cave or dug tunnel laid as=" + group + " before it";
			Func<string, int, int> num = (k, d) => opt.ContainsKey(k) ? Mathf.Clamp(Mathf.RoundToInt(F(opt[k])), 0, 20) : d;
			int vines = num("vines", 2);
			// (wet=1: the wet floor counts too - a flooded cave with an air pocket in it, a cave of an island sunk under the sea:
			// Biome Seven's root room 18 m down, Caisson Yard's flooded builders' ramp, w03 11-09)
			bool wetOk = opt.ContainsKey("wet") && F(opt["wet"]) >= 0.5f;
			int[] left = { num("dirt", 3), num("mush", 3), num("stone", 2), num("titanium", 0), num("light", 0) };
			string[] kind = { "dirt", "mush", "stone", "titanium", "light" };
			// (sea: the h= base, as Place reads it; wet: the sea's surface in the editor's world)
			float sea = DynamicIslands.EditorWaterLevel, wet = DynamicIslands.EditorSeaInWorld;
			// The spots inside: rock over them (not sky), a dry floor under them, 5 m and more from any spot open to the sky
			var open = new List<Vector3>();
			var spots = new List<KeyValuePair<Vector3, Vector3>>(); // (chest point, along the way)
			int nearOpen = 0, noRoof = 0, noFloor = 0, wetN = 0, all = 0;
			foreach (List<Vector3> line in lines)
				foreach (Vector3 p in line) if (!FirstSolid(p, Vector3.up, 40f).HasValue) open.Add(p);
			for (int li = 0; li < lines.Count; li++)
			{
				List<Vector3> line = lines[li];
				for (int i = 0; i < line.Count; i++)
				{
					Vector3 p = line[i];
					if (li == 0 && i < 5) continue;
					all++;
					if (open.Any(o => (o - p).magnitude < 5f)) { nearOpen++; continue; }
					RaycastHit? up = FirstSolid(p, Vector3.up, 8f), dn = FirstSolid(p, Vector3.down, 4f);
					if (!up.HasValue) { noRoof++; continue; }
					if (!dn.HasValue) { noFloor++; continue; }
					if (dn.Value.point.y < wet + 0.3f && !wetOk) { wetN++; continue; }
					if (spots.Any(s => (s.Key - p).magnitude < 0.9f)) continue;
					Vector3 along = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
					along.y = 0f;
					spots.Add(new KeyValuePair<Vector3, Vector3>(p, along.sqrMagnitude > 0.01f ? along.normalized : Vector3.forward));
				}
			}
			if (spots.Count == 0) return "caveloot: no dry spot inside the cave " + group + " (rock over it, 5 m from the mouth; of " + all + " points along it " + nearOpen + " near open sky, " + noRoof + " no roof, " + noFloor + " no floor, " + wetN + " wet - wet=1 lets the wet floor count)";
			// The kinds in turn - dirt, mushroom, stone, ... - so each part of the cave gets some of each
			var kinds = new List<string>();
			while (left.Any(x => x > 0))
				for (int k = 0; k < left.Length; k++) if (left[k] > 0) { kinds.Add(kind[k]); left[k]--; }
			int made = 0, mushN = 0, vineN = 0, skipped = 0;
			for (int k = 0; k < kinds.Count; k++)
			{
				var s = spots[Mathf.Min(spots.Count - 1, (int)((k + 0.5f) / kinds.Count * spots.Count))];
				Vector3 side = Vector3.Cross(Vector3.up, s.Value).normalized * (k % 2 == 0 ? 1f : -1f);
				RaycastHit? wall = FirstSolid(s.Key + Vector3.down * 0.5f, side, 4f);
				float off = wall.HasValue ? Mathf.Max(0f, wall.Value.distance - 0.6f) * 0.7f : 0.8f;
				RaycastHit? fl = FirstSolid(s.Key + side * off, Vector3.down, 4f);
				if (!fl.HasValue || Vector3.Angle(fl.Value.normal, Vector3.up) > 35f) fl = FirstSolid(s.Key, Vector3.down, 4f);
				if (!fl.HasValue || (fl.Value.point.y < wet + 0.3f && !wetOk)) { skipped++; continue; }
				string obj = kinds[k] == "dirt" ? "Pickup_Landmark_DirtPickup" : kinds[k] == "mush" ? "Pickup_Landmark_Mushroom " + (1 + mushN++ % 3)
					: kinds[k] == "stone" ? "Pickup_Landmark_Rock_2_Land" : kinds[k] == "titanium" ? "Pickup_Landmark_Titanium_Land" : "Placeable_Lantern_Metal";
				Vector2 f = FrameXZ(fl.Value.point);
				var o = new Dictionary<string, string> { { "h", (fl.Value.point.y - sea).ToString("0.###", Inv) }, { "sit", "1" }, { "yaw", ((k * 137) % 360).ToString(CultureInfo.InvariantCulture) } };
				EditorGameObject e;
				string err = Place(obj, f.x, f.y, o, out e);
				if (err != null) return "caveloot: " + err;
				if (e == null) { skipped++; continue; }
				AddToGroup(group + "_loot", e);
				made++;
			}
			// Vines from the roof, between the loot spots: where the roof leaves room for one (2.1 m clear under it), the
			// nearest such spot to its share of the way; its body (not its pivot) under the roof there
			Func<int, int, KeyValuePair<Vector3, Vector2>?> roofAt = (i, sgn) =>
			{
				var s = spots[i];
				Vector3 side = Vector3.Cross(Vector3.up, s.Value).normalized * sgn;
				RaycastHit? wall = FirstSolid(s.Key + Vector3.up * 0.5f, side, 4f);
				Vector3 at = s.Key + side * (wall.HasValue ? Mathf.Max(0f, wall.Value.distance - 0.5f) * 0.6f : 0.6f);
				RaycastHit? roof = FirstSolid(at, Vector3.up, 8f), fl = FirstSolid(at, Vector3.down, 4f);
				if (!roof.HasValue || !fl.HasValue) return null;
				return new KeyValuePair<Vector3, Vector2>(at, new Vector2(roof.Value.point.y, fl.Value.point.y + 2.1f));
			};
			var used = new List<Vector3>();
			for (int k = 0; k < vines; k++)
			{
				int want = Mathf.Min(spots.Count - 1, (int)((k + 1f) / (vines + 1) * spots.Count)), sgn = k % 2 == 0 ? -1 : 1;
				KeyValuePair<Vector3, Vector2>? first = null;
				for (int d = 0; d < spots.Count && first == null; d++)
					foreach (int i in new[] { want + d, want - d })
						if (i >= 0 && i < spots.Count && first == null)
						{
							var r = roofAt(i, sgn);
							if (r.HasValue && r.Value.Value.x - r.Value.Value.y >= 0.3f && !used.Any(u => (u - r.Value.Key).magnitude < 2f)) first = r;
						}
				if (first == null) { skipped++; continue; }
				Vector3 at = first.Value.Key;
				float top = first.Value.Value.x, lowest = first.Value.Value.y;
				Vector2 f = FrameXZ(at);
				var o = new Dictionary<string, string> { { "h", (top - sea).ToString("0.###", Inv) }, { "yaw", ((k * 97) % 360).ToString(CultureInfo.InvariantCulture) } };
				EditorGameObject e;
				string err = Place("CaveVine" + (1 + vineN++ % 3), f.x, f.y, o, out e);
				if (err != null) return "caveloot: " + err;
				if (e == null) { skipped++; continue; }
				Bounds b = ShapeBounds(e.gameObject);
				float h0 = b.size.y;
				// (too long for the room here even at 0.3 of its size: the nearest spot with room enough for it)
				if (h0 > 0.01f && h0 * 0.3f > top + 0.2f - lowest)
				{
					KeyValuePair<Vector3, Vector2>? better = null;
					for (int d = 1; d < spots.Count && better == null; d++)
						foreach (int i in new[] { want + d, want - d })
							if (i >= 0 && i < spots.Count && better == null)
							{
								var r = roofAt(i, sgn);
								if (r.HasValue && h0 * 0.3f <= r.Value.Value.x + 0.2f - r.Value.Value.y && !used.Any(u => (u - r.Value.Key).magnitude < 2f)) better = r;
							}
					if (better == null) { CommandUndoRedo.UndoRedoManager.Execute(new ObjectVisibilityCommand(new List<GameObject> { e.gameObject }, false)); skipped++; continue; }
					at = better.Value.Key; top = better.Value.Value.x; lowest = better.Value.Value.y;
				}
				// (short enough to walk under: a body's height of room under it)
				if (h0 > 0.01f && h0 > top + 0.2f - lowest) e.transform.localScale *= Mathf.Max(0.3f, (top + 0.2f - lowest) / h0);
				b = ShapeBounds(e.gameObject);
				e.transform.position += new Vector3(at.x - b.center.x, top + 0.2f - b.max.y, at.z - b.center.z);
				used.Add(at);
				AddToGroup(group + "_loot", e);
				made++;
			}
			placed += made;
			Log("  caveloot " + group + ": " + made + " placed on " + spots.Count + " dry spots inside" + (skipped > 0 ? ", " + skipped + " left out (no floor or roof)" : ""));
			return null;
		}

		/// <summary>cavegate: Raft's machete vines across the mouth of the cave laid as=group (alias as=, else group_vines);
		/// at=x,z: across its way nearest that point instead (a gated side gallery inside).</summary>
		static string CaveGateStep(string group, Dictionary<string, string> opt, ref int placed)
		{
			Vector3 mouth, inDir;
			CaveTool.Layout l;
			if (opt.ContainsKey("at"))
			{
				// at=x,z: across the passage at the dug or laid way nearest that point (a side gallery of a dug system:
				// its branch belongs to the same tunnel, so the group's first mouth would be the wrong place)
				string[] p = opt["at"].Split(',');
				if (p.Length != 2) return "cavegate: at= wants x,z";
				Vector2 want = new Vector2(F(p[0]), F(p[1]));
				List<List<Vector3>> lines = CaveLines(group);
				List<Vector3> best = null;
				int bi = -1;
				float bd = float.MaxValue;
				foreach (List<Vector3> line in lines)
					for (int i = 0; i < line.Count; i++)
					{
						float d = (FrameXZ(line[i]) - want).sqrMagnitude;
						if (d < bd) { bd = d; best = line; bi = i; }
					}
				if (best == null || best.Count < 2) return "cavegate: no cave or dug tunnel laid as=" + group + " before it";
				if (bd > 36f) return "cavegate: at=" + opt["at"] + " is " + Mathf.Sqrt(bd).ToString("0.#", Inv) + " m from the ways of " + group + " (6 m at most)";
				mouth = best[bi] + Vector3.down * 1.2f;
				inDir = best[Mathf.Min(bi + 1, best.Count - 1)] - best[Mathf.Max(bi - 1, 0)];
			}
			else if (recipeCaves.TryGetValue(group, out l) && l != null) { mouth = l.Mouth; inDir = l.Flat; }
			else
			{
				List<List<Vector3>> lines = CaveLines(group);
				if (lines.Count == 0 || lines[0].Count < 2) return "cavegate: no cave or dug tunnel laid as=" + group + " before it";
				mouth = lines[0][0] + Vector3.down * 1.2f;
				inDir = lines[0][Mathf.Min(2, lines[0].Count - 1)] - lines[0][0];
			}
			inDir.y = 0f;
			if (inDir.sqrMagnitude < 0.001f) return "cavegate: the cave " + group + " has no way in";
			inDir.Normalize();
			string alias = opt.ContainsKey("as") ? opt["as"] : group + "_vines";
			Vector3 at = opt.ContainsKey("at") ? mouth : mouth + inDir * 0.3f;
			Vector2 f = FrameXZ(at);
			var o = new Dictionary<string, string> { { "h", (at.y - DynamicIslands.EditorWaterLevel).ToString("0.###", Inv) } };
			if (opt.ContainsKey("scale")) o["scale"] = opt["scale"];
			EditorGameObject e;
			string err = Place(ContentCatalog.MacheteVines, f.x, f.y, o, out e);
			if (err != null) return "cavegate: " + err;
			if (e == null) return "cavegate: the vines were left out";
			// (as CICaveVineGate checked them, 11-04: standing straight, across the way in, their foot at the mouth's floor)
			e.transform.position = at;
			e.transform.rotation = Quaternion.Euler(0f, Quaternion.LookRotation(inDir).eulerAngles.y + (opt.ContainsKey("yaw") ? F(opt["yaw"]) : 0f), 0f);
			if (e.Props == null) e.Props = new Dictionary<string, string>();
			e.Props[BehaviourProps.Name] = alias;
			RecipeObjects[alias] = e;
			AddToGroup(alias, e);
			placed++;
			return null;
		}
	}
}
