using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's cave pieces (Balboa's dens, the tunnels) are rock shells seen from inside only: from outside you look through
	/// their sides and under their thin edges, and they hang over sloping ground. In a world, an island with cave pieces gets
	/// one rock mountain over them (AU83): a surface draped over the shells' roofs that slopes down to the ground, walled where
	/// it is cut off, with the passages' open ends kept clear and a skirt under their floors down to the ground.
	/// </summary>
	public class CaveMountain : MonoBehaviour
	{
		/// <summary>Name of the mountain's object (under the island).</summary>
		public const string SkinName = "CaveSkin";
		/// <summary>Grid step, how steep the sides fall (m down per m out), how far they reach, and how far above the roofs.</summary>
		public const float CellSize = Cell;
		const float Cell = 1.25f, Slope = 1.5f, Reach = 16f, Over = 0.3f;
		/// <summary>The hill's fall bowed: Slope times (HillBow + m out / HillCurve) a m - as Slope 5 m out.</summary>
		const float HillBow = 0.75f, HillCurve = 20f;
		/// <summary>More softening passes over the hill's flanks at least RidgeClear m from every roof and corridor.</summary>
		const int RidgePasses = 4; const float RidgeClear = 1.3f;

		readonly List<Transform> caves = new List<Transform>();
		readonly List<PropInfo> infos = new List<PropInfo>();
		readonly List<IDictionary<string, string>> props = new List<IDictionary<string, string>>();
		float due = -1f;
		GameObject skin;
		/// <summary>The hill's renderer once built (lone dens' walls are drawn as it outside - CaveTool.WallLikeHill), else null.</summary>
		public MeshRenderer Skin { get { return skin != null ? skin.GetComponent<MeshRenderer>() : null; } }

		/// <summary>A cave piece came and the mountain isn't (re)built yet (the tests wait for it instead of a fixed wait).</summary>
		public bool Pending { get { return due >= 0f; } }

		/// <summary>A cave piece spawned in a world under this island: (re)build the island's mountain once the island is all there.</summary>
		public static void Add(Transform island, GameObject piece, string name, IDictionary<string, string> props = null)
		{
			if (island == null || piece == null) return;
			if (!Wants(name)) return;
			PropInfo p = RaftProps.Get(name);
			CaveMountain m = island.GetComponent<CaveMountain>() ?? island.gameObject.AddComponent<CaveMountain>();
			// (lit as the cave tool's dens are: warm and dim, not by the blue sky probe outside - a blue den under a brown hill, 11-03)
			CaveLit.Mark(piece);
			m.caves.Add(piece.transform);
			m.infos.Add(p);
			m.props.Add(props);
			m.due = Time.realtimeSinceStartup + 1f;
		}

		/// <summary>A cave piece that needs a mountain over it (the Vines cave is a whole rock with its own outside already).</summary>
		public static bool Wants(string name)
		{
			PropInfo p = RaftProps.Get(name);
			return p != null && p.IsCave && name != "BalboaCave_Vines";
		}

		/// <summary>Tests: a map of the last build (R roof, s side, c a way in, - cut off out past an open end, g lower than the ground, . nothing), one line per row.</summary>
		public string LastMap;
		static char[,] Why;
		public int OverDug; // (cells over an open dug passage left without hill - tests)
		public int SeaCutCells; // (last build: cells cut for the sea - cave.seacut - tests)

		/// <summary>Tests: builds it again now.</summary>
		public void Rebuild() { due = -1f; Build(); }

		/// <summary>Tests: each open end's mouth over the sea - where its roof ends, on its floor, and the way out (CICaveIslands'
		/// mouth pictures, 11-08: every cave entrance looked at from outside).</summary>
		public List<KeyValuePair<Vector3, Vector3>> MouthViews()
		{
			var views = new List<KeyValuePair<Vector3, Vector3>>();
			foreach (Mouth m in lastMouths)
				for (int end = 0; end < 2; end++)
				{
					if (end == 0 ? !m.Plus : !m.Minus) continue;
					float dir = end == 0 ? 1f : -1f, start = end == 0 ? m.StartPlus : m.StartMinus;
					if (start > (end == 0 ? m.OwnPlus : m.OwnMinus) + 2.5f || m.FloorY < 0.3f) continue;
					Vector3 at = m.Pos + m.Along * (m.InsideAlong + dir * start) + m.Side * m.InsideSide;
					at.y = m.FloorY;
					views.Add(new KeyValuePair<Vector3, Vector3>(at, m.Along * dir));
				}
			return views;
		}

		/// <summary>
		/// Tests: every open end's way in, looked along from 5 m out to past where its roof ends at knee, chest and head height over
		/// its floor - where the mountain is in the way (a play walking in stuck on it - AU83). Empty: all clear.
		/// </summary>
		public List<string> BlockedWays()
		{
			var bad = new List<string>();
			Collider mine = skin != null ? skin.GetComponent<Collider>() : null;
			if (mine == null) return bad;
			Physics.SyncTransforms();
			foreach (Mouth m in lastMouths)
				for (int end = 0; end < 2; end++)
				{
					if (end == 0 ? !m.Plus : !m.Minus) continue;
					float dir = end == 0 ? 1f : -1f, start = end == 0 ? m.StartPlus : m.StartMinus;
					// (an end joined on to another piece: the roof runs on past it to the chain's mouth, which that piece checks at its
					// own floor - this one's floor there was a tunnel slope's height off, under the way in's floor, 10-26)
					if (start > (end == 0 ? m.OwnPlus : m.OwnMinus) + 2.5f) continue;
					foreach (float up in new[] { 0.5f, 1.2f, 1.9f })
						foreach (float s in new[] { 0f, -m.HalfWidth * 0.4f, m.HalfWidth * 0.4f })
						{
							Vector3 from = m.Pos + m.Along * (m.InsideAlong + dir * (start + 5f)) + m.Side * (m.InsideSide + s);
							from.y = m.FloorY + up;
							RaycastHit hit;
							if (mine.Raycast(new Ray(from, -dir * m.Along), out hit, 6f))
								bad.Add(m.Name + (end == 0 ? " +" : " -") + " end, " + up.ToString("0.0") + " m over its floor, " + s.ToString("0.0") + " m aside: the hill " + hit.distance.ToString("0.0") + " m in" + HitShape(hit, m));
						}
					// Out in front beside the way in: no hill over the ground there (it spread on over the ledge and the paths - Sundown
					// Canyons, the Iron Gate); over the cave's own rock, or another cave's hill reaching there, is all right
					foreach (float f in new[] { 1.5f, 3f, 5f, 8f })
						foreach (float s in new[] { 1f, 4f, 8f, -1f, -4f, -8f })
						{
							float aside = s + Mathf.Sign(s) * m.HalfWidth;
							Vector3 at = m.Pos + m.Along * (m.InsideAlong + dir * (start + f)) + m.Side * (m.InsideSide + aside);
							if (OtherHill(at)) continue;
							RaycastHit[] hits = UnderSkin(at, m.FloorY + 40f, mine);
							if (hits == null || hits[0].point.y - hits[1].point.y <= 0.6f) continue;
							// (over the cave's own rock - a shell's flank out past its roof - or at its edge, within a step: the hill
							// covering it, not the ground; the generator's dead-end caves)
							bool rock = false;
							foreach (Vector3 by in new[] { Vector3.zero, Vector3.right * Cell, Vector3.left * Cell, Vector3.forward * Cell, Vector3.back * Cell })
							{
								RaycastHit[] near = by == Vector3.zero ? hits : UnderSkin(at + by, m.FloorY + 40f, mine);
								if (near != null && OwnRock(near[1].collider)) { rock = true; break; }
							}
							if (rock) continue;
							bad.Add(m.Name + (end == 0 ? " +" : " -") + " end: hill in front, " + f.ToString("0.0") + " m out, " + aside.ToString("0.0") + " m aside, " + (hits[0].point.y - hits[1].point.y).ToString("0.0") + " m over the " + hits[1].collider.name);
						}
				}
			return bad;
		}

		/// <summary>(tests) What the ray hit: the face's slant and its corners against the floor and the roof's end.</summary>
		string HitShape(RaycastHit hit, Mouth m)
		{
			var mc = hit.collider as MeshCollider;
			string s = " (normal " + hit.normal.ToString("F2");
			if (mc != null && mc.sharedMesh != null && hit.triangleIndex >= 0)
			{
				Vector3[] v = mc.sharedMesh.vertices; int[] t = mc.sharedMesh.triangles;
				for (int c = 0; c < 3; c++)
				{
					Vector3 w = mc.transform.TransformPoint(v[t[hit.triangleIndex * 3 + c]]);
					float a = Vector3.Dot(w - m.Pos, m.Along) - m.InsideAlong;
					s += ", corner along " + a.ToString("0.00") + " aside " + (Vector3.Dot(w - m.Pos, m.Side) - m.InsideSide).ToString("0.00") + " y-floor " + (w.y - m.FloorY).ToString("0.00");
				}
			}
			return s + "; roof ends " + m.StartPlus.ToString("0.00") + "/" + m.StartMinus.ToString("0.00") + ")";
		}

		bool[,] otherHill; // (last build: out past an open end, where another cave's hill reaches)
		float hillX0, hillZ0;
		// (tests: the last build's cells, for CICaveCell)
		float[,] dTop, dRim, dGround, dRaw, dHeight, dCover, dCorr; bool[,] dCut, dSea, dOn; string[,] dGroundBy;

		/// <summary>Tests: what the last build made of the cell nearest a world point (CICaveCell), or null off the grid.</summary>
		public string CellText(Vector3 at)
		{
			if (dTop == null) return null;
			int i = Mathf.RoundToInt((at.x - hillX0) / Cell), k = Mathf.RoundToInt((at.z - hillZ0) / Cell);
			if (i < 0 || k < 0 || i >= dTop.GetLength(0) || k >= dTop.GetLength(1)) return null;
			System.Func<float, string> F = v => float.IsNaN(v) ? "-" : float.IsInfinity(v) ? "inf" : v.ToString("0.00");
			return "cell " + i + "," + k + " (" + F(hillX0 + i * Cell) + " " + F(hillZ0 + k * Cell) + "): top " + F(dTop[i, k]) + " rim " + F(dRim[i, k]) + " ground " + F(dGround[i, k]) +
				" (" + (dGroundBy[i, k] ?? "-") + ") cover " + F(dCover[i, k]) + " corridor " + F(dCorr[i, k]) + " raw " + F(dRaw[i, k]) + " height " + F(dHeight[i, k]) +
				(dCut[i, k] ? " CUT" : "") + (dSea[i, k] ? " SEA" : "") + (dOn[i, k] ? " ON" : "") + MouthsText(new Vector3(hillX0 + i * Cell, 0f, hillZ0 + k * Cell));
		}

		// (dev: each mouth's along/side distance from its inside point at a spot, past its start, and half width)
		string MouthsText(Vector3 at)
		{
			var s = new System.Text.StringBuilder();
			foreach (Mouth m in lastMouths)
			{
				Vector3 d = at - m.Pos;
				float a = Vector3.Dot(d, m.Along) - m.InsideAlong, sd = Vector3.Dot(d, m.Side) - m.InsideSide;
				s.Append(" | ").Append(m.Name).Append(" a ").Append(a.ToString("0.0")).Append(" s ").Append(sd.ToString("0.0")).Append(" hw ").Append(m.HalfWidth.ToString("0.0"))
					.Append(m.Plus ? " +" + m.StartPlus.ToString("0.0") : "").Append(m.Minus ? " -" + m.StartMinus.ToString("0.0") : "");
			}
			return s.ToString();
		}

		/// <summary>What a ray down at a spot hits, highest first, when the hill is the top and something is under it; else null.</summary>
		static RaycastHit[] UnderSkin(Vector3 at, float fromY, Collider skin)
		{
			RaycastHit[] hits = Physics.RaycastAll(new Vector3(at.x, fromY, at.z), Vector3.down, 120f, ~0, QueryTriggerInteraction.Ignore);
			System.Array.Sort(hits, (x, y) => y.point.y.CompareTo(x.point.y));
			return hits.Length >= 2 && hits[0].collider == skin ? hits : null;
		}

		bool OwnRock(Collider c)
		{
			foreach (Transform t in caves)
				if (t != null && c.transform.IsChildOf(t)) return true;
			return false;
		}

		bool OtherHill(Vector3 at)
		{
			if (otherHill == null) return false;
			int i = Mathf.RoundToInt((at.x - hillX0) / Cell), k = Mathf.RoundToInt((at.z - hillZ0) / Cell);
			return i >= 0 && k >= 0 && i < otherHill.GetLength(0) && k < otherHill.GetLength(1) && otherHill[i, k];
		}

		void Update()
		{
			if (due < 0f || Time.realtimeSinceStartup < due) return;
			due = -1f;
			try { Build(); }
			catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Cave mountain: " + e.Message); }
		}

		/// <summary>One cave piece's open ends: where the mountain must leave the way in clear.</summary>
		struct Mouth { public Vector3 Pos, Along, Side; public float InsideAlong, InsideSide, HalfWidth, FloorY, OwnPlus, OwnMinus, StartPlus, StartMinus, Front, ArchHalf, ArchUp, DomeMax; public bool Plus, Minus, Dry; public string Name; }
		readonly List<Mouth> lastMouths = new List<Mouth>();

		void Build() { Build(Reach); }

		/// <summary>The hill over the pieces on a grid <pad> m past their bounds each way (Reach; more when a dry den's dome reaches
		/// further: Build tries again once with a pad that holds it).</summary>
		void Build(float pad)
		{
			if (skin != null) { DestroyImmediate(skin); skin = null; } // (its collider must not count as ground)
			var cols = new HashSet<Collider>();
			var floorOf = new Dictionary<Collider, float>();
			var ownerOf = new Dictionary<Collider, int>(); // (which cave piece: its mouth's index)
			var mouths = new List<Mouth>();
			Bounds b = new Bounds();
			bool any = false;
			Renderer look = null;
			float lookSize = 0f;
			for (int i = 0; i < caves.Count; i++)
			{
				Transform t = caves[i];
				if (t == null) continue;
				PropInfo p = infos[i];
				// (the piece's measures at its scale: a cave set bigger or smaller kept its floor, inside point and way in's width at
				// the measured size. Measured at the scale it spawns with - its catalog copy's, 3.008 for the bear den - so only the
				// scale over that one counts: by its whole scale the den's floor went 9 m under its real one, its open floor counted
				// as roof and its inside point lay 8 m out past its mouth, and the hill ran out over the ground before it - Stormwing's
				// strip, Balboa Remade's path, f72b954)
				Vector3 sc = OverMeasured(t, p.Name);
				float floorY = t.position.y + p.Floor * sc.y;
				foreach (Collider c in t.GetComponentsInChildren<Collider>())
					if (c.enabled && !c.isTrigger && c.name != CaveTool.MouthName && c.name != CaveTool.JoinName) { cols.Add(c); floorOf[c] = floorY; ownerOf[c] = mouths.Count; }
				foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
				{
					if (!r.enabled || !(r is MeshRenderer) || r.name == CaveTool.MouthName || r.name == CaveTool.JoinName) continue;
					if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
					float s = r.bounds.size.x * r.bounds.size.z;
					if (s > lookSize && r.sharedMaterial != null) { lookSize = s; look = r; }
				}
				Vector3 along = t.rotation * (p.Axis == 0 ? Vector3.right : Vector3.forward);
				along.y = 0f; along.Normalize();
				Vector3 side = new Vector3(along.z, 0f, -along.x);
				Vector3 inside = t.position + t.rotation * Vector3.Scale(p.Inside, sc);
				// (how far the piece itself reaches from its inside point, each way along its passage: past that its roof is another
				// piece's, joined on)
				float ax = p.Axis == 0 ? sc.x : sc.z, mid = p.Axis == 0 ? p.Centre.x : p.Centre.z, half = (p.Axis == 0 ? p.Size.x : p.Size.z) / 2f, inAxis = p.Axis == 0 ? p.Inside.x : p.Inside.z;
				float ownPlus = (mid + half - inAxis) * ax, ownMinus = (inAxis - (mid - half)) * ax;
				mouths.Add(new Mouth
				{
					Pos = t.position, Along = along, Side = side,
					InsideAlong = Vector3.Dot(inside - t.position, along), InsideSide = Vector3.Dot(inside - t.position, side),
					HalfWidth = p.Width * (p.Axis == 0 ? sc.z : sc.x) / 2f + 1f, FloorY = floorY, OwnPlus = ownPlus, OwnMinus = ownMinus, Plus = p.OpenPlus, Minus = p.OpenMinus, Name = t.name,
					Dry = IsDryFoot(i < props.Count ? props[i] : null, p),
					DomeMax = DomeMaxFor(i < props.Count ? props[i] : null),
					// (a lone den: its mouth's plane, where its wall stands - see LoneDenFront)
					Front = LoneDenFront(t, p, i < props.Count ? props[i] : null, along, Vector3.Dot(inside - t.position, along)),
					ArchHalf = CaveTool.ArchHalf * OverMeasured(t, CaveTool.Room).x, ArchUp = CaveTool.ArchUp * OverMeasured(t, CaveTool.Room).x
				});
			}
			// (the three lists side by side: one gone from all of them)
			for (int i = caves.Count - 1; i >= 0; i--)
				if (caves[i] == null) { caves.RemoveAt(i); if (i < infos.Count) infos.RemoveAt(i); if (i < props.Count) props.RemoveAt(i); }
			if (!any || look == null || cols.Count == 0) return;

			Physics.SyncTransforms();
			int nx = Mathf.CeilToInt((b.size.x + 2f * pad) / Cell) + 1, nz = Mathf.CeilToInt((b.size.z + 2f * pad) / Cell) + 1;
			float x0 = b.min.x - pad, z0 = b.min.z - pad, rayTop = b.max.y + 5f, rayLen = b.size.y + 60f;
			var top = new float[nx, nz];       // the shells' roofs (NaN: none)
			var ground = new float[nx, nz];    // what is under them otherwise
			var groundBy = new string[nx, nz]; // (tests: what that is)
			var corridor = new float[nx, nz];  // a way in: the floor height there (NaN: not one)
			var rimY = new float[nx, nz];      // a shell's low edge over lower ground, not a roof (NaN: none)
			var floorAt = new float[nx, nz];   // the shell's floor (its highest point under the roof line; NaN: none)
			var mouthAt = new float[nx, nz];   // in front of an open end, as wide as the passage: its floor height (NaN: not)
			var own = new int[nx, nz];         // whose roof or low edge it is (-1: none)
			int groundMask = (int)LayerMasks.MASK_GroundMask_NonRaft | (1 << IslandSpawner.TerrainLayer);
			bool backfaces = Physics.queriesHitBackfaces;
			Physics.queriesHitBackfaces = true; // (the shells face inwards: from above, a roof is its back)
			try
			{
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						Vector3 at = new Vector3(x0 + i * Cell, rayTop, z0 + k * Cell);
						float roof = float.NaN, low = float.NaN, gr = float.NegativeInfinity;
						int roofBy = -1, lowBy = -1;
						foreach (RaycastHit h in Physics.RaycastAll(at, Vector3.down, rayLen, ~0, QueryTriggerInteraction.Ignore))
						{
							float f;
							if (floorOf.TryGetValue(h.collider, out f))
							{
								// (only a roof counts: a floor open to the sky is the way in)
								if (h.point.y > f + 1.5f && !(h.point.y <= roof)) { roof = h.point.y; roofBy = ownerOf[h.collider]; }
								else if (!(h.point.y <= low)) { low = h.point.y; lowBy = ownerOf[h.collider]; } // (the shell below its floor too: Balboa's den goes on 11 m under it)
							}
							else if ((groundMask & (1 << h.collider.gameObject.layer)) != 0 && h.point.y > gr) { gr = h.point.y; groundBy[i, k] = h.collider.name; }
						}
						top[i, k] = roof;
						floorAt[i, k] = low;
						ground[i, k] = float.IsInfinity(gr) ? b.min.y - 2f : gr;
						// (a shell's edge under the roof line hung over the ground beside it: a 1.5 m slot into the den - AU83)
						rimY[i, k] = float.IsNaN(roof) && low > ground[i, k] + 0.3f ? low : float.NaN;
						own[i, k] = !float.IsNaN(roof) ? roofBy : lowBy;
					}
			}
			finally { Physics.queriesHitBackfaces = backfaces; }
			// The ways in: from where the roof ends along the passage, out (not from the inside point: a stretch of the den
			// with no roof over it was left open, and the den showed through the hill from above - AU83)
			// (a way in follows its own chain's roof - the pieces joined end to end - and stops at another cave's roof: a straight
			// cave's room ended by the old mine's mound, its roof run went on over the mound and its way in ran on past it, a
			// slot 3 cells wide down the mound's far side to the sea floor, under water traps under the hill - Old Mine Islet 11-05)
			int[] chain = Chains(mouths);
			var startPlus = new float[mouths.Count];
			var startMinus = new float[mouths.Count];
			var stopPlus = new float[mouths.Count];
			var stopMinus = new float[mouths.Count];
			for (int j = 0; j < mouths.Count; j++)
			{
				startPlus[j] = RoofEnd(mouths[j], 1f, top, own, chain, j, x0, z0, nx, nz);
				startMinus[j] = RoofEnd(mouths[j], -1f, top, own, chain, j, x0, z0, nx, nz);
				stopPlus[j] = WayStop(mouths[j], 1f, startPlus[j], top, own, chain, j, x0, z0, nx, nz);
				stopMinus[j] = WayStop(mouths[j], -1f, startMinus[j], top, own, chain, j, x0, z0, nx, nz);
			}
			// (cave.foot=dry: a way in runs at most DryWay past its roof. Shelter Atoll's lagoon cave's way in ran on some 100 m
			// over the islet and past the ammo den's west side: a slot through the den's hill, its back open to the land, 11-07)
			// (a lone den: its way in starts at its mouth's plane, past the end of its roof - the hill ended 2.3 m inside the den's
			// mouth, so the den's own front - its ceiling's lip, its side walls, its floor - stood open in a box cut in the hill, round
			// the wall across its mouth, 11-09 user. Half a square past it: the hill's face just in front of the wall, not behind it)
			for (int j = 0; j < mouths.Count; j++)
				if (!float.IsNaN(mouths[j].Front) && mouths[j].Plus && mouths[j].Front + Cell * 0.5f > startPlus[j])
				{
					float grow = mouths[j].Front + Cell * 0.5f - startPlus[j];
					startPlus[j] += grow;
					stopPlus[j] += grow;
				}
			bool dryFootOn = mouths.Any(mm => mm.Dry);
			if (dryFootOn)
				for (int j = 0; j < mouths.Count; j++)
				{
					stopPlus[j] = Mathf.Min(stopPlus[j], startPlus[j] + DryWay);
					stopMinus[j] = Mathf.Min(stopMinus[j], startMinus[j] + DryWay);
				}
			// How far out past each cave's open ends (m past where its roof ends; under 0.5: not out past them). The hill of a
			// cave's roof stops there, a face of rock across its front: it spread on over the ledge and the paths before the
			// mouth, beside the way in (Sundown Canyons' switchbacks, the Iron Gate's shoulder - AU83)
			var ahead = new float[mouths.Count][,];
			for (int j = 0; j < mouths.Count; j++) ahead[j] = new float[nx, nz];
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					corridor[i, k] = float.NaN;
					mouthAt[i, k] = float.NaN;
					Vector3 at = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell);
					for (int j = 0; j < mouths.Count; j++)
					{
						float a = Vector3.Dot(at - mouths[j].Pos, mouths[j].Along) - mouths[j].InsideAlong;
						ahead[j][i, k] = Mathf.Max(mouths[j].Plus ? a - startPlus[j] : float.NegativeInfinity, mouths[j].Minus ? -a - startMinus[j] : float.NegativeInfinity);
					}
					for (int j = 0; j < mouths.Count; j++)
					{
						Mouth m = mouths[j];
						Vector3 d = at - m.Pos;
						float a = Vector3.Dot(d, m.Along) - m.InsideAlong, s = Vector3.Dot(d, m.Side) - m.InsideSide;
						// (between the inside point and the corridor, as wide as the passage itself: where the roof ends unevenly,
						// a square with no roof there was hill with a wall down to the ground across the way in - Ranger's Rest)
						// (a lone den's only up to its arch: out past it the way in is the sand's, and the skirt under its floor stood from the
						// sand up to the floor's height beside it as a dark straight wall, Caisson Yard and Shelter Atoll 11-09 r53-r56)
						bool upToArch = float.IsNaN(m.Front) || (m.Plus ? a <= startPlus[j] + Cell : a >= -startMinus[j] - Cell);
						if (upToArch && Mathf.Abs(s) < m.HalfWidth - 1f && ((m.Plus && a > 0f) || (m.Minus && a < 0f)) && float.IsNaN(mouthAt[i, k])) mouthAt[i, k] = m.FloorY;
						if (!float.IsNaN(top[i, k])) continue;
						// (a lone den's way in out past its wall only as wide as its arch: the hill comes up to the arch's rim, not a
						// trench the den's width cut in front of it, a box notch round the arch, 11-09 user r41)
						if (Mathf.Abs(s) < (float.IsNaN(m.Front) ? m.HalfWidth : m.ArchHalf + LoneSlot) && ((m.Plus && a > startPlus[j] && a < stopPlus[j]) || (m.Minus && a < -startMinus[j] && a > -stopMinus[j]))) { corridor[i, k] = m.FloorY; break; }
					}
				}
			lastMouths.Clear();
			for (int j = 0; j < mouths.Count; j++) { Mouth m = mouths[j]; m.StartPlus = startPlus[j]; m.StartMinus = startMinus[j]; lastMouths.Add(m); }

			// What the surface must stay over: the highest roof or low edge in the square and the ones round it (a shell's edge
			// between two samples stuck out of a surface that fell away from the last roof sampled: a 2.2 m slot under it - AU83)
			// (not out past a cave's open end from the roof beside it: a square of hill before the mouth)
			var cover = new float[nx, nz];
			var coverBy = new int[nx, nz];
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					float c0 = float.NaN;
					int by = -1;
					for (int a = Mathf.Max(i - 1, 0); a <= Mathf.Min(i + 1, nx - 1); a++)
						for (int c = Mathf.Max(k - 1, 0); c <= Mathf.Min(k + 1, nz - 1); c++)
						{
							float y = float.IsNaN(top[a, c]) ? rimY[a, c] : top[a, c];
							if (float.IsNaN(y) || (y <= c0)) continue;
							if ((a != i || c != k) && own[a, c] >= 0 && ahead[own[a, c]][i, k] > 0.5f) continue;
							c0 = y; by = own[a, c];
						}
					cover[i, k] = c0;
					coverBy[i, k] = by;
				}
			float[,] dome = dryFootOn ? DryDomes(mouths, startPlus, startMinus, cover, own, ground, x0, z0, nx, nz) : null;
			// (a den's dome reaches 40-60 m out, the grid only Reach past the shells: the dome was cut off at the grid's edge, a flat
			// tan wall metres over the sand, and sunk there a steep straight ramp, the hill a spire - Shelter Atoll 11-09 r62. Once:
			// a grid wide enough for the whole dome, so its bell runs out into the ground on its own)
			if (dome != null && pad <= Reach && DomeAtEdge(dome, ground, nx, nz))
			{
				Debug.Log("[CUSTOM ISLANDS] Cave mountain: dome reaches the grid's edge, grid widened to " + (LastDomeReach + DomePad).ToString("0") + " m");
				Build(Mathf.Max(Reach, LastDomeReach + DomePad));
				return;
			}
			// The surface: over a roof, the highest roof around it; elsewhere falling away from the roofs nearby
			var height = new float[nx, nz];
			var on = new bool[nx, nz];
			int reach = Mathf.CeilToInt(Reach / Cell);
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++) height[i, k] = float.NegativeInfinity;
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (float.IsNaN(cover[i, k]) || !float.IsNaN(corridor[i, k])) continue;
					if (dome != null && coverBy[i, k] >= 0 && mouths[coverBy[i, k]].Dry && !DomeCapped(mouths[coverBy[i, k]]) && !float.IsNaN(dome[i, k])) continue; // (its dome's; a cave.dome one's roof keeps its slopes)
					float peak = cover[i, k] + Over;
					float[,] past = coverBy[i, k] >= 0 ? ahead[coverBy[i, k]] : null;
					for (int di = -reach; di <= reach; di++)
						for (int dk = -reach; dk <= reach; dk++)
						{
							int a = i + di, c = k + dk;
							if (a < 0 || c < 0 || a >= nx || c >= nz) continue;
							float dist = Cell * Mathf.Sqrt(di * di + dk * dk);
							// (curved: gentler near the top, steeper lower down - a straight fall made each roof cell's cone a flat slab
							// with a sharp edge where two met, Caisson Yard over the sea, 11-08 r26 user)
							float h = peak - Slope * dist * (HillBow + dist / HillCurve);
							// (cave.foot=dry: on dry land its last FlareHigh m flare out into the sand - the den hill's flanks
							// came down at the full slope and met the sand at a hard, steep line, Shelter Atoll from the east, 11-08)
							// (11-08 b: a straight flare left flat, angular sand wedges - now a curve that leaves the hill at its own
							// slope and runs out ever flatter, dipping under the sand FlareSink m down)
							if (dryFootOn && ground[a, c] > SeaLevel + DryBy && peak > ground[a, c] + FlareHigh)
								h = Mathf.Max(h, ground[a, c] - FlareSink + (FlareHigh + FlareSink)
									* Mathf.Exp(-Slope * (dist - (peak - ground[a, c] - FlareHigh) / Slope) / (FlareHigh + FlareSink)));
							if (past != null && past[a, c] > 0.5f && (di != 0 || dk != 0))
							{
								// (cave.foot=dry, that piece's own: beside its opening the hill goes on out past the mouth's plane as a
								// shoulder, falling to the ground towards the opening too - the den's front corners stood as flat
								// sheer faces with square corners at the mouth's plane, Shelter Atoll's ammo den from the east, 11-07)
								Mouth dm = mouths[coverBy[i, k]];
								if (!dm.Dry) continue;
								float off = Mathf.Abs(Vector3.Dot(new Vector3(x0 + a * Cell, 0f, z0 + c * Cell) - dm.Pos, dm.Side) - dm.InsideSide) - dm.HalfWidth - DryFront;
								// (on dry land only: out over the sea it stood as slabs under the water)
								if (off <= 0f || !(ground[a, c] > SeaLevel + DryBy)) continue;
								h = SoftMin(h, ground[a, c] + ShoulderSlope * off); // (gentler than the hill: its inner faces by the opening stood steep and dark, 11-08)
							}
							if (h > height[a, c]) height[a, c] = h;
						}
				}
			if (dome != null)
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
						if (dome[i, k] > height[i, k]) height[i, k] = dome[i, k];
			// (rounded off, and a little uneven: a hill, not cones; never below a roof)
			var raw = (float[,])height.Clone();
			for (int pass = 0; pass < 2; pass++)
			{
				var soft = new float[nx, nz];
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						float sum = 0f; int n = 0;
						for (int di = -2; di <= 2; di++)
							for (int dk = -2; dk <= 2; dk++)
							{
								int a = Mathf.Clamp(i + di, 0, nx - 1), c = Mathf.Clamp(k + dk, 0, nz - 1);
								sum += Mathf.Max(height[a, c], ground[a, c] - 2f); n++;
							}
						soft[i, k] = sum / n;
					}
				height = soft;
			}
			// (and the flanks well away from every roof rounded more: two cones' flat faces met at a sharp ridge, a flat
			// triangular slab with a hard edge over the sea - Caisson Yard, 11-08 r26/r31 user)
			{
				int clear = Mathf.CeilToInt(RidgeClear / Cell);
				var nearRoof = new bool[nx, nz];
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						if (float.IsNaN(cover[i, k]) && float.IsNaN(corridor[i, k])) continue;
						for (int a = Mathf.Max(i - clear, 0); a <= Mathf.Min(i + clear, nx - 1); a++)
							for (int c = Mathf.Max(k - clear, 0); c <= Mathf.Min(k + clear, nz - 1); c++) nearRoof[a, c] = true;
					}
				for (int pass = 0; pass < RidgePasses; pass++)
				{
					var soft = (float[,])height.Clone();
					for (int i = 0; i < nx; i++)
						for (int k = 0; k < nz; k++)
						{
							if (nearRoof[i, k] || float.IsNegativeInfinity(height[i, k])) continue;
							float sum = 0f; int n = 0;
							for (int di = -2; di <= 2; di++)
								for (int dk = -2; dk <= 2; dk++)
								{
									int a = Mathf.Clamp(i + di, 0, nx - 1), c = Mathf.Clamp(k + dk, 0, nz - 1);
									sum += Mathf.Max(height[a, c], ground[a, c] - 2f); n++;
								}
							soft[i, k] = sum / n;
						}
					height = soft;
				}
			}
			// (out past an open end with no rock of its own and no other hill there: none - the rounding had spread the hill
			// on over it again; a face down to the ground stands at the cut)
			var cut = new bool[nx, nz];
			Why = new char[nx, nz]; // (tests: what last set each cell, in LastMap)
			var cutAhead = new bool[nx, nz]; // (cut only for lying out past a mouth over lower hill: a lone den's funnel may still ramp there)
			OverDug = 0;
			var sea = new bool[nx, nz];
			string seaOpt;
			bool seaCutOn = props.Any(p => p != null && p.TryGetValue(ObjectProps.CaveSeaCut, out seaOpt) && seaOpt == "1");
			SeaCutCells = 0;
			otherHill = new bool[nx, nz]; hillX0 = x0; hillZ0 = z0;
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					bool past = false;
					for (int j = 0; j < mouths.Count && !past; j++) past = ahead[j][i, k] > 0.5f;
					cut[i, k] = past && float.IsNaN(cover[i, k]) && !(raw[i, k] > ground[i, k] - 0.6f);
					cutAhead[i, k] = cut[i, k];
					if (cut[i, k]) Why[i, k] = 'k';
					// (over a dug passage open to the sky - the dug rock is the highest ground there - none: the hill stood on over
					// Ironreef's mouth C as a bridge 5-13 m over the dug cut, its underside blue-white in the shade - RF3, 11-05)
					if (float.IsNaN(top[i, k]) && float.IsNaN(corridor[i, k]) && groundBy[i, k] != null && groundBy[i, k].StartsWith(DigTunnel.RockName, System.StringComparison.Ordinal)) { cut[i, k] = true; cutAhead[i, k] = false; otherHill[i, k] = false; OverDug++; }
					// (nor out over the sea from a roof above it: the hill's skin over the water, its foot on the sea floor, closed a
					// pool of sea under it - a swimmer under a roof with no air, Old Mine Islet's mound over its south shore 11-05;
					// a cave's own roof under the sea keeps its hill, and the hill's surface under the water too)
					// (a cell kept only by the roof beside it counts as uncovered here: that one-cell margin past a shell's edge over the
					// water was the flap the 11-05 face stood under - its wall down to the sea floor shut the water under it, Old Mine
					// Islet's -7 -36 trap after the first sea cut; its own roof or low edge still keeps a cell)
					bool bare = float.IsNaN(top[i, k]) && float.IsNaN(rimY[i, k]);
					// (round 4, 11-05: and where the hill's surface was under the water but the rounding lifted it to the water line
					// over a sea floor well under it - its neighbours on a pad over the shore pulled it up - a lid at the surface over
					// a pool of sea: Old Mine Islet's -51.5 8.2 trap off the miners' row, skin at 0 over a 6-8 m deep floor)
					// (only on a hill whose island asks for it - cave.seacut on one of its pieces: the cut took the whole sea side of
					// Shelter Atoll's islet, a hill raised from the lagoon floor, down to sheer dark faces; cavemerge 11-06)
					if (seaCutOn && !cut[i, k] && bare && SeaCut(raw[i, k], height[i, k], ground[i, k])) { cut[i, k] = true; sea[i, k] = true; SeaCutCells++; }
					otherHill[i, k] = past && float.IsNaN(cover[i, k]) && !cut[i, k];
				}
			// (and its shore ring: the face down from the last cell left to a cut one leant out over the water, a hollow of sea
			// under it with its mouth under the water - Old Mine Islet's south shore trap at -7 -36, 11-05; the face now stands
			// on the shore, the ground there over the sea or nearly)
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (cut[i, k] || !float.IsNaN(top[i, k]) || !float.IsNaN(rimY[i, k]) || !(raw[i, k] > SeaLevel) || !(ground[i, k] < SeaLevel + SeaShoreBy)) continue;
					bool bySea = false;
					for (int di = -1; di <= 1 && !bySea; di++)
						for (int dk = -1; dk <= 1 && !bySea; dk++)
						{
							int a = i + di, c = k + dk;
							bySea = a >= 0 && a < nx && c >= 0 && c < nz && sea[a, c];
						}
					if (bySea) { cut[i, k] = true; otherHill[i, k] = false; }
				}
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (cut[i, k]) { height[i, k] = ground[i, k] - 3.5f; on[i, k] = false; continue; }
					float x = x0 + i * Cell, z = z0 + k * Cell;
					float rough = Mathf.Clamp01((height[i, k] - ground[i, k]) / 3f); // (none at its foot: no bumps through the ground around)
					height[i, k] += rough * ((Mathf.PerlinNoise(x * 0.13f, z * 0.13f) - 0.5f) * 2.2f + (Mathf.PerlinNoise(x * 0.4f + 7f, z * 0.4f) - 0.5f) * 0.6f);
					if (!float.IsNaN(cover[i, k]) && float.IsNaN(corridor[i, k]) && height[i, k] < cover[i, k] + Over) height[i, k] = cover[i, k] + Over;
					// (its foot goes on a little under the ground: no edge to see where it meets it; and only where the hill itself
					// comes near the ground - the rounding alone lifted rock out of a pit or a dug stair far from the cave, Ashfall's crypt)
					on[i, k] = float.IsNaN(corridor[i, k]) && height[i, k] > ground[i, k] - 0.6f && raw[i, k] > ground[i, k] - 0.6f - 2f * Slope * Cell;
				}
			// (cave.foot=dry: out past a mouth the hill's sides by its opening slope up from its edge at ShoulderSlope - the shoulders'
			// inner faces stood as sheer dark walls the shell's whole height, the den's other pieces' roofs reaching them unclamped,
			// Shelter Atoll's ammo den 11-08; the strip beside the opening the cut took is a low ramp now, the opening itself stays cut)
			if (dryFootOn) ShoulderRamps(mouths, ahead, cover, top, corridor, ground, height, on, cut, cutAhead, x0, z0, nx, nz);
			// (one more ring round it, sunk under the ground: its edge slopes into the ground, not a stepped wall over it)
			var rim = new List<int>();
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					// (a cell the sea cut took, now dry land, gets it too: the skin there stood on the sand on a dark stepped wall, a
					// row of teeth along its foot - Shelter Atoll's den, its flanks raised from the sea, 11-07; not out past a mouth; only on a
					// hill one of whose pieces has cave.foot=dry)
					if (on[i, k] || !float.IsNaN(corridor[i, k])) continue;
					if (cut[i, k])
					{
						if (!dryFootOn) continue;
						// (past a mouth only right in front of its opening, within its way in: a mouth's "ahead" is a whole half-plane, and
						// the lagoon tunnel's line 100 m off cut the den hill's whole foot, so it kept its stepped walls - teeth on the east
						// foot, a dark wall along the west one, 11-07 third review)
						bool pastMouth = false;
						Vector3 cellAt = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell);
						for (int j = 0; j < mouths.Count && !pastMouth; j++)
							pastMouth = ahead[j][i, k] > 0.5f && ahead[j][i, k] < DryWay && Mathf.Abs(Vector3.Dot(cellAt - mouths[j].Pos, mouths[j].Side) - mouths[j].InsideSide) < mouths[j].HalfWidth + DryFront;
						if (pastMouth || !(ground[i, k] > SeaLevel + DryBy)) continue;
					}
					bool next = false;
					for (int di = -1; di <= 1 && !next; di++)
						for (int dk = -1; dk <= 1 && !next; dk++)
						{
							int a = i + di, c = k + dk;
							next = a >= 0 && c >= 0 && a < nx && c < nz && on[a, c];
						}
					if (next) rim.Add(i * nz + k);
				}
			foreach (int r in rim)
			{
				int i = r / nz, k = r % nz;
				height[i, k] = ground[i, k] - 0.8f;
				on[i, k] = true; Why[i, k] = 'r';
			}
			// (cave.foot=dry: three more softening passes after the rim - its foot stood on the sand on a dark wall, and the cells off
			// a roof's flat stood well under it, a jagged skyline of square steps along the den hill's top, 11-07 third review; a
			// cell over a roof stays over its cover, the sunk rim stays sunk)
			if (dryFootOn)
			{
				var isRim = new bool[nx, nz];
				foreach (int r in rim) isRim[r / nz, r % nz] = true;
				// (a roof cell between higher ones goes halfway up to the highest beside it: the den shell's ridged top showed through
				// as a row of square teeth on the skyline)
				var floor = new float[nx, nz];
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						if (float.IsNaN(cover[i, k])) continue;
						float up = cover[i, k];
						for (int di = -1; di <= 1; di++)
							for (int dk = -1; dk <= 1; dk++)
							{
								int a = i + di, c = k + dk;
								if (a >= 0 && c >= 0 && a < nx && c < nz && cover[a, c] > up) up = cover[a, c];
							}
						floor[i, k] = (cover[i, k] + up) / 2f + Over;
					}
				// (round a roof's edge the skin falls at most EdgeDrop of a slope's step a cell for EdgeRings cells: the roof's flat edge
				// went down a full step (1.9 m) to the cells beside it in a staircase round the shell's top, and seen from low on
				// the den's west side its corners stood up as a row of square teeth on the skyline, 11-07 den round 2; it only lifts)
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
						if (float.IsNaN(cover[i, k])) floor[i, k] = float.NaN;
				for (int ring = 0; ring < EdgeRings; ring++)
				{
					var held = (float[,])floor.Clone();
					for (int i = 0; i < nx; i++)
						for (int k = 0; k < nz; k++)
							for (int di = -1; di <= 1; di++)
								for (int dk = -1; dk <= 1; dk++)
								{
									int a = i + di, c = k + dk;
									if (a < 0 || c < 0 || a >= nx || c >= nz || float.IsNaN(held[a, c])) continue;
									float f = held[a, c] - EdgeDrop * Slope * Cell * ((di != 0 && dk != 0) ? 1.41f : 1f);
									if (float.IsNaN(floor[i, k]) || f > floor[i, k]) floor[i, k] = f;
								}
				}
				for (int pass = 0; pass < FootPasses; pass++)
				{
					var was = (float[,])height.Clone();
					for (int i = 0; i < nx; i++)
						for (int k = 0; k < nz; k++)
						{
							if (!on[i, k] || isRim[i, k] || !float.IsNaN(corridor[i, k])) continue;
							float sum = 0f; int n = 0;
							for (int di = -1; di <= 1; di++)
								for (int dk = -1; dk <= 1; dk++)
								{
									int a = i + di, c = k + dk;
									if (a < 0 || c < 0 || a >= nx || c >= nz || !on[a, c]) continue;
									sum += was[a, c]; n++;
								}
							if (n > 0) height[i, k] = float.IsNaN(floor[i, k]) ? sum / n : Mathf.Max(sum / n, floor[i, k]);
						}
				}
			}
			// (and no loose bits: a few cells of skin cut off from the hill, over no cave, stood alone on the shore as a slab with a
			// wall under it - by Shelter Atoll's ammo den, where the cut past its mouth left one at its corner, 11-07)
			if (dryFootOn) ShoulderRamps(mouths, ahead, cover, top, corridor, ground, height, on, cut, cutAhead, x0, z0, nx, nz); // (again: the passes above lifted them)
			LooseDropped = dryFootOn ? DropLoose(on, top, height, ground, nx, nz) : 0; // (only on a hill with cave.foot=dry)

			var map = new System.Text.StringBuilder();
			for (int k = nz - 1; k >= 0; k--)
			{
				for (int i = 0; i < nx; i++)
					map.Append(!float.IsNaN(corridor[i, k]) ? 'c' : on[i, k] ? (!float.IsNaN(top[i, k]) ? 'R' : 's') : cut[i, k] ? '-' : float.IsNegativeInfinity(height[i, k]) || height[i, k] < ground[i, k] - 3f ? '.' : 'g');
				map.Append('\n');
			}
			// (tests: skin height over the ground round each way in, m: 0-9, + over 9, _ under, c the way in, . no skin)
			map.Append("height over ground near the ways in:\n");
			for (int k = nz - 1; k >= 0; k--)
			{
				bool row = false;
				for (int i = 0; i < nx && !row; i++)
					for (int kb = Mathf.Max(k - 8, 0); kb <= Mathf.Min(k + 8, nz - 1) && !row; kb++) row = !float.IsNaN(corridor[i, kb]);
				if (!row) continue;
				for (int i = 0; i < nx; i++)
				{
					float d = height[i, k] - ground[i, k];
					map.Append(!float.IsNaN(corridor[i, k]) ? 'c' : !on[i, k] ? '.' : d < 0f ? '_' : d > 9.5f ? '+' : (char)('0' + Mathf.RoundToInt(d)));
				}
				map.Append('\n');
			}
			map.Append("what set each cell near the ways in (k cut, w way, o sea, x no hill behind, n past LoneRun, L lone ramp, S shoulder, r rim, E edge cap, G grid edge):\n");
			for (int k = nz - 1; k >= 0; k--)
			{
				bool row = false;
				for (int i = 0; i < nx && !row; i++)
					for (int kb = Mathf.Max(k - 8, 0); kb <= Mathf.Min(k + 8, nz - 1) && !row; kb++) row = !float.IsNaN(corridor[i, kb]);
				if (!row) continue;
				for (int i = 0; i < nx; i++) map.Append(!float.IsNaN(corridor[i, k]) ? 'c' : Why[i, k] == (char)0 ? (on[i, k] ? 's' : '.') : Why[i, k]);
				map.Append('\n');
			}
			var tall = new Dictionary<string, int>();
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
					if (!on[i, k] && float.IsNaN(corridor[i, k]) && height[i, k] < ground[i, k] - 3f && groundBy[i, k] != null)
						tall[groundBy[i, k]] = (tall.ContainsKey(groundBy[i, k]) ? tall[groundBy[i, k]] : 0) + 1;
			dTop = top; dRim = rimY; dGround = ground; dRaw = raw; dHeight = height; dCover = cover; dCorr = corridor; dCut = cut; dSea = sea; dOn = on; dGroundBy = groundBy;
			LastMap = "loose skin left out: " + LooseDropped + " cells\nhigher ground: " + string.Join(", ", System.Linq.Enumerable.Select(tall, kv => kv.Key + " " + kv.Value)) + "\nx " + x0.ToString("0.0") + " z " + z0.ToString("0.0") + " step " + Cell + " (rows from +z)\n" + map;
			var verts = new List<Vector3>();
			var norms = new List<Vector3>();
			var uvs = new List<Vector2>();
			var tris = new List<int>();
			Vector3 origin = new Vector3(b.center.x, b.min.y, b.center.z);
			System.Func<int, int, Vector3> P = (i, k) => new Vector3(x0 + i * Cell, height[i, k], z0 + k * Cell);
			// Top: both sides (from inside a cave, a hole in its shell shows rock). Its normals are the surface's at each grid point
			// (from the heights round it), shared by the squares that meet there: each square's own flat one lit the steep sides
			// as a patchwork, and Balboa's rock shader (Mountains) blends its rock and sand by the normal, so each square came out
			// orange or black on its own (11-06; the mosaic of 45-degree rectangles over Shelter Atoll's ammo cave was the den's
			// front wall, though - CaveTool.PlugOpening)
			var gn = new Vector3[nx, nz];
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (!on[i, k]) continue;
					int il = i > 0 && on[i - 1, k] ? i - 1 : i, ir = i + 1 < nx && on[i + 1, k] ? i + 1 : i;
					int kd = k > 0 && on[i, k - 1] ? k - 1 : k, ku = k + 1 < nz && on[i, k + 1] ? k + 1 : k;
					float sx = ir > il ? (height[ir, k] - height[il, k]) / ((ir - il) * Cell) : 0f;
					float sz = ku > kd ? (height[i, ku] - height[i, kd]) / ((ku - kd) * Cell) : 0f;
					gn[i, k] = new Vector3(-sx, 1f, -sz).normalized;
				}
			for (int i = 0; i + 1 < nx; i++)
				for (int k = 0; k + 1 < nz; k++)
				{
					if (!on[i, k] || !on[i + 1, k] || !on[i, k + 1] || !on[i + 1, k + 1]) continue;
					Vector3 a = P(i, k), c = P(i + 1, k), d = P(i + 1, k + 1), e = P(i, k + 1);
					Vector3 n = Vector3.Cross(e - a, c - a).normalized;
					Quad(verts, norms, uvs, tris, origin, a, e, d, c, n, true, new[] { gn[i, k], gn[i, k + 1], gn[i + 1, k + 1], gn[i + 1, k] });
				}
			// Walls where the surface is cut off: down to the ground; at a way in, only a skirt under its floor
			var arch = new Arch { Shells = cols, Box = b, Own = own, Ahead = ahead, Mouths = mouths, Top = top };
			bool backs = Physics.queriesHitBackfaces;
			try
			{
				for (int i = 0; i + 1 < nx; i++)
					for (int k = 0; k + 1 < nz; k++)
					{
						if (!on[i, k] || !on[i + 1, k] || !on[i, k + 1] || !on[i + 1, k + 1]) continue;
						Edge(i, k, i + 1, k, 0, -1, nx, nz, on, top, height, ground, corridor, mouthAt, floorAt, P, verts, norms, uvs, tris, origin, arch, gn);
						Edge(i, k + 1, i + 1, k + 1, 0, 1, nx, nz, on, top, height, ground, corridor, mouthAt, floorAt, P, verts, norms, uvs, tris, origin, arch, gn);
						Edge(i, k, i, k + 1, -1, 0, nx, nz, on, top, height, ground, corridor, mouthAt, floorAt, P, verts, norms, uvs, tris, origin, arch, gn);
						Edge(i + 1, k, i + 1, k + 1, 1, 0, nx, nz, on, top, height, ground, corridor, mouthAt, floorAt, P, verts, norms, uvs, tris, origin, arch, gn);
					}
			}
			finally { Physics.queriesHitBackfaces = backs; }
			if (tris.Count == 0) return;

			var mesh = new Mesh { name = SkinName };
			if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(verts);
			mesh.SetNormals(norms);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(tris, 0);
			mesh.RecalculateBounds();
			skin = new GameObject(SkinName);
			skin.layer = IslandSpawner.TerrainLayer;
			skin.transform.position = origin;
			skin.transform.SetParent(transform, true);
			// (drawn from both sides: from inside a cave, through a gap in its shell, the mountain's underside is rock too - X7)
			skin.AddComponent<MeshFilter>().sharedMesh = TwoSided(mesh, verts, norms, uvs, tris);
			var mr = skin.AddComponent<MeshRenderer>();
			// (the island's own biggest rock, so a snow island gets snowy rock; else Raft's big rock - the shells' own is mostly dirt)
			// (cave.rock=plain: Raft's plain big rock whatever the island has - Shelter Atoll's biggest rock is a brown cracked one that
			// read as metal over its den; user 11-08: one plain mountain rock)
			string plainOpt;
			bool plain = props.Any(p => p != null && p.TryGetValue(ObjectProps.CaveRock, out plainOpt) && plainOpt == "plain");
			Renderer rockLook = plain ? null : IslandRock();
			if (rockLook == null)
			{
				GameObject rock = PlaceableCatalog.Get("BigRock_1");
				rockLook = rock != null ? rock.GetComponentInChildren<MeshRenderer>(true) : null;
			}
			mr.sharedMaterial = rockLook != null && rockLook.sharedMaterial != null ? rockLook.sharedMaterial : look.sharedMaterial;
			mr.shadowCastingMode = look.shadowCastingMode;
			mr.receiveShadows = look.receiveShadows;
			skin.AddComponent<MeshCollider>().sharedMesh = mesh;
			// (lit as its caves are - warm, dim, through its own light probe - not by Raft's blue sky light (Trilight x8): out of
			// Balboa its steep faces showed bright blue and patterned, on a mine's mound and through the gaps of a den's back;
			// user 11-06, Old Mine Islet. The piece's own backs were lit so already; the hill wasn't)
			CaveLit.Mark(skin);
			// (tinted as its caves are: a dark den under an untinted orange hill showed where the one ended and the other began -
			// the first tinted cave's tint, multiplied into the island's rock as ApplyTint does - RK15)
			// (Raft's own island rock - CaravanIslandRocks - has no colour to tint: it then stays as it is; the den's rock material
			// stretched over the hill looked blotchy, grey-blue patches - tried 11-03)
			IDictionary<string, string> tinted = props.FirstOrDefault(ObjectProps.HasTint);
			if (tinted != null)
			{
				Color factor = Color.Lerp(Color.white, ObjectProps.Tint(tinted), ObjectProps.TintStrength(tinted));
				int n = ObjectProps.ApplyTint(skin, ObjectProps.Tint(tinted), ObjectProps.TintStrength(tinted));
				if (n == 0) TintRockTextures(mr, factor);
				Debug.Log("[CUSTOM ISLANDS] Cave mountain tint " + factor + " (colour " + n + ")");
			}
			else
			{
				// (cave.rock=den: the hill's rock as the den's own - Balboa's sandy rock showed blue-grey on its steep faces round the
				// brown den, the den's facets through the skin as tan shards; Shelter Atoll's ammo cave, review 11-06)
				string rockOpt;
				int asDen = props.FindIndex(p => p != null && p.TryGetValue(ObjectProps.CaveRock, out rockOpt) && rockOpt == "den");
				if (asDen >= 0 && caves[asDen] != null) RockAsDen(mr, caves[asDen].gameObject);
			}
			DressTunnels();
			Debug.Log("[CUSTOM ISLANDS] Cave mountain over " + caves.Count + " cave piece(s): " + verts.Count + " points, " + mr.sharedMaterial.name + ", bounds " + mr.bounds.min.ToString("0") + "-" + mr.bounds.max.ToString("0") + (OverDug > 0 ? ", " + OverDug + " cell(s) left open over dug passages" : "") + (SeaCutCells > 0 ? ", " + SeaCutCells + " sea cell(s) cut (cave.seacut)" : ""));
			// (lone dens walled before the hill was built: their walls' outsides drawn as the hill now)
			foreach (Transform t in caves)
				if (t != null) CaveTool.WallLikeHill(t.gameObject, mr);
		}

		/// <summary>
		/// The hill's rock coloured as a den's shell (cave.rock=den): the skin drawn lit with its rock texture alone (the sand and
		/// other colour textures swapped for it for the measure) against the den's shell (CaveTool.LitColour), and its rock
		/// texture alone multiplied by the one over the other - the sand on its gentle slopes stays. False when the material has
		/// no texture named rock or a colour could not be read.
		/// </summary>
		internal const float DenRockGrey = 0.35f;

		/// <summary>cave.foot=dry: each cell out past a dry mouth (within DryWay + 4 m of its roof's end, on dry land, off any roof
		/// or way in) is kept at most ShoulderSlope a m over the ground, less 0.6, for each m it lies to the side past the opening's
		/// half width (cut cells stay cut: turning them into skin left dark slivers at the sand, 11-08).</summary>
		static void ShoulderRamps(List<Mouth> mouths, float[][,] ahead, float[,] cover, float[,] top, float[,] corridor, float[,] ground, float[,] height, bool[,] on, bool[,] cut, bool[,] cutAhead, float x0, float z0, int nx, int nz)
		{
			var near = new bool[nx, nz];
			for (int j = 0; j < mouths.Count; j++)
			{
				Mouth m = mouths[j];
				if (!m.Dry) continue;
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						float past = ahead[j][i, k];
						// (a lone den's: out past its mouth's plane a cell over no roof is ramped even if a roof beside it set its cover - the
						// hill beside the opening stood on out past the den's wall as two sheer walls the roof's height, a box cut in the
						// hill round the arch, 11-09 user r41/r44)
						bool lone = !float.IsNaN(m.Front) && past > 0f && float.IsNaN(top[i, k]);
						if (!(past > -BackBand) || past > DryWay + 4f || (!float.IsNaN(cover[i, k]) && !lone) || !float.IsNaN(corridor[i, k])) continue;
						// (and over the cells the cut took in front of it, where the hill's own shape lay under the sand: only the one row
						// of skin past the plane was left, sunk by the edge cap, so the dome stood full height right at the plane - a flat
						// sheer wall the den's width across with the arch at its foot, cut cells either side of the way in where the den's
						// flat front showed, and vertical cheeks - Shelter Atoll 11-09..11-11 r63-r69; only where the hill stands behind)
						bool aheadCut = lone && cut[i, k] && cutAhead[i, k];
						if (lone && (!cut[i, k] || aheadCut))
						{
							// (a lone den: the hill goes on out past its wall and falls to the sand ahead, its cheeks right at the arch's
							// rim as high as the arch and rising off it to the hill's face - not cut back to a sheer face the den's width
							// across with the arch's wall standing in a box notch in it, 11-09 user r41-r45)
							Vector3 cp = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell);
							float sl = Mathf.Abs(Vector3.Dot(cp - m.Pos, m.Side) - m.InsideSide) - (m.ArchHalf + LoneSlot);
							if (sl < 0f) { Why[i, k] = 'w'; continue; }
							// (not out over the sea: there it stood as a flat square panel at the sea's foot, Caisson Yard and Shelter Atoll r48)
							if (!on[i, k] && !(ground[i, k] > SeaLevel + DryBy)) { Why[i, k] = 'o'; continue; }
							float lf = Mathf.Max(ground[i, k], SeaLevel - SeaFootBy);
							Vector3 bp = cp - m.Along * ((m.Plus ? 1f : -1f) * (past + Cell));
							int bi = Mathf.RoundToInt((bp.x - x0) / Cell), bk = Mathf.RoundToInt((bp.z - z0) / Cell);
							bool behind = bi >= 0 && bk >= 0 && bi < nx && bk < nz && on[bi, bk];
							if (aheadCut && !behind) { Why[i, k] = 'x'; continue; }
							float face = behind ? height[bi, bk] : m.FloorY + m.ArchUp;
							float want = Mathf.Max(Mathf.Min(face, m.FloorY + m.ArchUp * Mathf.Clamp01(1f - past / LoneEdge) + LoneSide * sl * (1f + sl / ShoulderCurve)) - LoneFall * past, Mathf.Min(lf, ground[i, k]) - 0.6f);
							if (past > LoneRun) { Why[i, k] = 'n'; continue; }
							// (within LoneSink m of the ground it dives into it, LoneDive times as steep: it lay just over the flat sand
							// as a pale flat panel with a hard edge, Caisson Yard and Shelter Atoll r48-r50)
							if (want - lf < LoneSink) want = Mathf.Max(lf + LoneSink - (LoneSink - (want - lf)) * LoneDive, Mathf.Min(lf, ground[i, k]) - 0.6f);
							height[i, k] = on[i, k] ? Mathf.Max(SoftMin(height[i, k], want), want) : want;
							on[i, k] = true; near[i, k] = true; cut[i, k] = false; Why[i, k] = 'L';
							continue;
						}
						// (over the sea too, from just under the water: the den hill's flank fell into the sea cut before the mouth as a
						// flat slab with a sharp edge, Caisson Yard 11-08 r26-r33 user)
						float foot = Mathf.Max(ground[i, k], SeaLevel - SeaFootBy);
						float sd = Mathf.Abs(Vector3.Dot(new Vector3(x0 + i * Cell, 0f, z0 + k * Cell) - m.Pos, m.Side) - m.InsideSide) - m.HalfWidth;
						if (sd <= 0f) continue;
						float back = Mathf.Max(-past, 0f); // (behind the opening's plane the cap rises fast: no seam where the ramps stop, 11-08 c)
						float ramp = foot - 0.6f + ShoulderSlope * sd * (1f + sd / ShoulderCurve) + BackSlope * back * (1f + back / ShoulderCurve); // (curved, not a plane: a plane showed as flat wedges)
						if (on[i, k] && !cut[i, k]) { if (ramp < height[i, k]) Why[i, k] = 'S'; height[i, k] = SoftMin(height[i, k], ramp); near[i, k] = true; } // (soft: a plain min left a crisp seam beside the mouth, 11-08 b)
					}
			}
			// (11-09: round a lone den the skin's edge goes into the ground, rising off it no faster than LoneEdgeRise per m in:
			// where it ended over the sand or past LoneRun it stood as a dark straight wall the floor's height over the sand beside
			// the raised sand, Caisson Yard, Shelter Atoll and Canopy Stair r53/r54 user; and over the sea its flat foot lay under
			// the water as a pale tan panel - now under the sea's floor too)
			bool anyLone = false;
			foreach (Mouth m in mouths) if (m.Dry && !float.IsNaN(m.Front)) anyLone = true;
			if (anyLone)
			{
				var dist = new float[nx, nz];
				// (the way in isn't an edge: measured from it too, the cheeks beside the arch were sunk into the ground right at its
				// sides, and the den's flat front wall stood bare round the arch down to the sand, a box notch with square sides,
				// Caisson Yard and Shelter Atoll 11-09 r57/r59 user)
				for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++) dist[i, k] = on[i, k] || !float.IsNaN(corridor[i, k]) ? 1e6f : 0f;
				float dd = Cell * 1.41421356f;
				for (int pass = 0; pass < 2; pass++)
					for (int i0 = 0; i0 < nx; i0++)
						for (int k0 = 0; k0 < nz; k0++)
						{
							int i = pass == 0 ? i0 : nx - 1 - i0, k = pass == 0 ? k0 : nz - 1 - k0, st = pass == 0 ? -1 : 1;
							float v = dist[i, k];
							if (v == 0f) continue;
							if (i + st < 0 || i + st >= nx || k + st < 0 || k + st >= nz || k - st < 0 || k - st >= nz) v = Mathf.Min(v, Cell); // (the grid's edge: off)
							else
							{
								v = Mathf.Min(v, dist[i + st, k] + Cell); v = Mathf.Min(v, dist[i, k + st] + Cell);
								v = Mathf.Min(v, dist[i + st, k + st] + dd); v = Mathf.Min(v, dist[i + st, k - st] + dd);
							}
							dist[i, k] = v;
						}
				int capped = 0;
				for (int j = 0; j < mouths.Count; j++)
				{
					Mouth m = mouths[j];
					if (!m.Dry || float.IsNaN(m.Front)) continue;
					for (int i = 0; i < nx; i++)
						for (int k = 0; k < nz; k++)
						{
							float past = ahead[j][i, k];
							if (!on[i, k] || cut[i, k] || !(past > 0f) || past > LoneRun + LoneEdgeBand || !float.IsNaN(top[i, k]) || !float.IsNaN(corridor[i, k])) continue;
							float cap = ground[i, k] - 0.6f + LoneEdgeRise * Mathf.Max(dist[i, k] - Cell, 0f);
							if (height[i, k] > cap) { height[i, k] = cap; near[i, k] = true; capped++; Why[i, k] = 'E'; }
						}
				}
				if (capped > 0) Debug.Log("[CUSTOM ISLANDS] Lone den skin edge sunk into the ground at " + capped + " cells");
			}
			// (the grid's own edge isn't the hill's: where the skin ran on to it, it was cut off there metres over the ground and
			// stood as a flat tan wall down to the sand, Shelter Atoll's sea side 11-09 r57/r60 user - near the grid's edge the
			// skin now goes into the ground like a lone den's edge, rising LoneEdgeRise per m in)
			{
				int sunk = 0;
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						if (!on[i, k] || cut[i, k] || !float.IsNaN(top[i, k]) || !float.IsNaN(corridor[i, k])) continue;
						float e = Mathf.Min(Mathf.Min(i, nx - 1 - i), Mathf.Min(k, nz - 1 - k)) * Cell;
						if (e > LoneEdgeBand) continue;
						float cap = ground[i, k] - 0.6f + LoneEdgeRise * e;
						if (height[i, k] > cap) { height[i, k] = cap; near[i, k] = true; sunk++; Why[i, k] = 'G'; }
					}
				if (sunk > 0) Debug.Log("[CUSTOM ISLANDS] Cave hill skin sunk into the ground at the grid's edge at " + sunk + " cells");
			}
			// (11-08 c: the shoulders' cells lowered toward their neighbours' mean a few times: the ridge where the shoulder cap meets the
			// hill's slope showed as a crisp edge with flat facets; only lowering, so no roof or way in is covered more)
			for (int pass = 0; pass < RoundPasses; pass++)
				for (int i = 1; i < nx - 1; i++)
					for (int k = 1; k < nz - 1; k++)
					{
						if (!near[i, k]) continue;
						float sum = 0f; int n = 0;
						for (int a = i - 1; a <= i + 1; a++)
							for (int c = k - 1; c <= k + 1; c++)
								if ((a != i || c != k) && on[a, c] && !cut[a, c]) { sum += height[a, c]; n++; }
						if (n >= 5) height[i, k] = Mathf.Min(height[i, k], Mathf.Max(sum / n, ground[i, k] - FlareSink));
					}
		}

		/// <summary>The lower of a and b, rounded off where they are within SeamSoft m of each other (no crease where two
		/// slopes meet).</summary>
		static float SoftMin(float a, float b)
		{
			float t = Mathf.Max(SeamSoft - Mathf.Abs(a - b), 0f) / SeamSoft;
			return Mathf.Min(a, b) - t * t * SeamSoft * 0.25f;
		}

		/// <summary>A lone bear den's (no cave.plug: CaveTool.PlugOpening walls its mouth) mouth's plane, m along its way in from its
		/// inside point; NaN for any other piece.</summary>
		static float LoneDenFront(Transform t, PropInfo info, IDictionary<string, string> p, Vector3 along, float insideAlong)
		{
			if (t == null || info == null || info.Name != CaveTool.Room) return float.NaN;
			string plug;
			if (p != null && p.TryGetValue(ObjectProps.CavePlug, out plug) && !string.IsNullOrEmpty(plug)) return float.NaN;
			float size = OverMeasured(t, CaveTool.Room).x;
			return Vector3.Dot(t.rotation * Vector3.forward * (CaveTool.RoomMouthZ * size), along) - insideAlong;
		}

		static bool IsDryFoot(IDictionary<string, string> p, PropInfo info = null)
		{
			return DryFootFor(p, info != null ? info.Name : null);
		}

		/// <summary>cave.foot=dry, or a bear den with no cave.foot of its own (11-08 user: every den's mouth as Shelter Atoll's -
		/// its floor slab stood square on the sand before the other dens; cave.foot=wet keeps the old foot).</summary>
		/// <summary>cave.dome=<m>: the most its dry dome may reach (NaN: unset or not a number above 0).</summary>
		internal static float DomeMaxFor(IDictionary<string, string> p)
		{
			string v; float r;
			if (p != null && p.TryGetValue(ObjectProps.CaveDome, out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) && r > 0f) return r;
			return float.NaN;
		}

		/// <summary>Is this mouth's dome cut down by cave.dome (its roof cells then keep the hill's own slopes too)?</summary>
		static bool DomeCapped(Mouth m) { return !float.IsNaN(m.DomeMax); }

		internal static bool DryFootFor(IDictionary<string, string> p, string name)
		{
			string v;
			if (p != null && p.TryGetValue(ObjectProps.CaveFoot, out v)) return v == "dry";
			return name == CaveTool.Room;
		}

		/// <summary>Skin pieces of at most this many cells, over no cave's roof, cut off from the rest are left out (DropLoose).</summary>
		internal const int LooseCells = 40;
		/// <summary>How far over the sea the ground of a cut cell must be for the hill's sunk rim to go on it (m).</summary>
		internal const float DryBy = 0.3f;
		// (cave.foot=dry: over the sea the shoulder ramps start this far under the water, m)
		const float SeaFootBy = 0.6f;
		// (cave.foot=dry: how far a way in runs past its roof's end, m)
		internal const float DryWay = 12f;
		// (cave.foot=dry: how far to the side of a mouth's opening the sunk rim stays off the cells out past it, m)
		internal const float DryFront = 2f;
		// (cave.foot=dry: softening passes over the skin off the roofs after its sunk rim)
		internal const int FootPasses = 3;
		// (cave.foot=dry: a cell round a roof's edge falls at most this part of a full slope's step a cell)
		internal const float EdgeDrop = 0.4f;
		// (cave.foot=dry: how many cells round a roof's edge fall by EdgeDrop)
		internal const int EdgeRings = 2;
		// (cave.foot=dry: on dry land the hill's last FlareHigh m over the ground curve out from Slope ever flatter and dip FlareSink m under the sand: a foot that flares out; SeamSoft: how wide SoftMin rounds a seam)
		internal const float FlareHigh = 3f, FlareSink = 0.3f, SeamSoft = 2.5f;
		// (cave.foot=dry: how steeply a shoulder beside a mouth's opening rises from the ground, m a m out from the opening's side)
		internal const float ShoulderSlope = 0.9f;
		// (cave.foot=dry: the shoulder cap curves up (steeper by 1/ShoulderCurve a m) and reaches BackBand m behind the opening's plane, rising BackSlope a m there)
		internal const float ShoulderCurve = 4f, BackSlope = 1.5f, BackBand = 6f;
		/// <summary>A lone den (CaveTool.PlugOpening's wall at its mouth): its way in past the wall is LoneSlot m wider than the
		/// arch each side; the hill beside it rises LoneSide a m off it and falls LoneFall a m out to the sand, for at most
		/// LoneRun m; right beside the way in it is the arch's height at the mouth and down to the floor LoneEdge m out (a funnel
		/// opening out to the sand: at the arch's height all along, the way in ran between two straight cut walls, 11-09 r47).</summary>
		internal const float LoneEdgeRise = 1.2f, LoneEdgeBand = 8f; // (how steeply a lone den's skin may rise off its edge, and how far past LoneRun that holds)
		internal const float LoneSlot = 0.3f, LoneSide = 1.2f, LoneFall = 1.7f, LoneRun = 14f, LoneEdge = 2f, LoneSink = 1.5f, LoneDive = 2.5f;
		// (cave.foot=dry: how many times the shoulders' cells are lowered toward their neighbours' mean)
		internal const int RoundPasses = 3;
		// (cave.foot=dry, 11-08 d: one rounded dome over the dry piece's whole shell instead of a cone from each roof cell - its
		// roof's high front corners stood as two steep towers either side of the mouth with a slot between them, Shelter Atoll)
		// DomeOver: rock over the roof (m, before the roughness); DomeFlank: how far past the shell the dome reaches at the sides
		// and back, DomeFront past its open end (m); DomeSink: how far under the ground its foot runs out
		internal const float DomeOver = 3.5f, DomeFlank = 6f, DomeFront = 4f, DomeSink = 0.6f, DomeHead = 1.15f;
		// (cave.foot=dry: beside its opening the face over the way in starts this high over its floor, m)
		internal const float DryHead = 2.6f;
		/// <summary>Tests: the last dry dome's height over its base and reach (m).</summary>
		public static string LastDome = "";
		/// <summary>The furthest a dome reaches from its den's inside point (m, the last DryDomes); DomePad: grid room past it.</summary>
		static float LastDomeReach;
		const float DomePad = 4f;

		/// <summary>Is a dome cut off at the grid's edge (an edge cell under it, over the ground)?</summary>
		static bool DomeAtEdge(float[,] dome, float[,] ground, int nx, int nz)
		{
			for (int i = 0; i < nx; i++)
				for (int k = 0; k < nz; k++)
				{
					if (i != 0 && k != 0 && i != nx - 1 && k != nz - 1) continue;
					float g = float.IsNegativeInfinity(ground[i, k]) ? SeaLevel - 30f : ground[i, k];
					if (!float.IsNaN(dome[i, k]) && dome[i, k] > g + 0.5f) return true;
				}
			return false;
		}

		/// <summary>cave.foot=dry: the height of one smooth dome over each dry piece's shell (NaN off every dome): an ellipse round
		/// the shell's cells (DomeFlank past them at the sides and back, DomeFront past its open end) under a (1 - u^2)^2 bell,
		/// tall enough to stay DomeOver over every one of its roof cells, its foot running out DomeSink under the ground (null: none).</summary>
		/// <summary>The dry dome's distance out (0 its middle, 1 its foot): a rounded square, so a box-shaped shell's corners are
		/// covered without a spire over its middle.</summary>
		static float DomeU(float s, float a) { s *= s; a *= a; return Mathf.Sqrt(Mathf.Sqrt(s * s + a * a)); }

		/// <summary>The dry dome's height share at <u>: 1 at its middle, flat on top, 0 at its foot, running out flat.</summary>
		static float Bell(float u) { u = Mathf.Clamp01(u); return 1f - u * u * (3f - 2f * u); }

		/// <summary>The u where Bell is <share> (Bell falls from 1 to 0).</summary>
		static float BellAt(float share)
		{
			float lo = 0f, hi = 1f;
			for (int n = 0; n < 24; n++) { float mid = (lo + hi) / 2f; if (Bell(mid) > share) lo = mid; else hi = mid; }
			return Mathf.Max(lo, 0.02f);
		}

		static float[,] DryDomes(List<Mouth> mouths, float[] startPlus, float[] startMinus, float[,] cover, int[,] own, float[,] ground, float x0, float z0, int nx, int nz)
		{
			float[,] dome = null;
			LastDome = ""; LastDomeReach = 0f;
			for (int j = 0; j < mouths.Count; j++)
			{
				Mouth m = mouths[j];
				if (!m.Dry || !(m.Plus || m.Minus)) continue;
				float f = m.Plus ? 1f : -1f;
				float sMin = float.PositiveInfinity, sMax = float.NegativeInfinity, aMin = float.PositiveInfinity, aMax = float.NegativeInfinity;
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						if (own[i, k] != j || float.IsNaN(cover[i, k])) continue;
						Vector3 d = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell) - m.Pos;
						float s = Vector3.Dot(d, m.Side) - m.InsideSide, a = f * (Vector3.Dot(d, m.Along) - m.InsideAlong);
						sMin = Mathf.Min(sMin, s); sMax = Mathf.Max(sMax, s); aMin = Mathf.Min(aMin, a); aMax = Mathf.Max(aMax, a);
					}
				if (float.IsInfinity(sMin)) continue;
				// (11-08 round 10: one height - the roof's top plus DomeOver - and the footprint widened until it covers every shell
				// cell; a rounded-square footprint and a bell profile (flat round top, its foot running out flat into the ground). The
				// height fitted to the corner cells instead made a 52 m spire)
				float cs = (sMin + sMax) / 2f, baseY = m.FloorY - 0.9f, top = float.NegativeInfinity;
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
						if (own[i, k] == j && !float.IsNaN(cover[i, k])) top = Mathf.Max(top, cover[i, k]);
				float tall = (top + DomeOver - baseY) * DomeHead; // (a little over what the roof needs: its top cell needs no point of a cone)
				float rs0 = (sMax - sMin) / 2f + DomeFlank, ca = 0f, rb = 0f, rf = 0f, rs = 0f, best = float.PositiveInfinity;
				for (float shift = -4f * Cell; shift <= 8f * Cell; shift += Cell)
				{
					float ca1 = (aMin + aMax) / 2f + shift, rb0 = ca1 - aMin + DomeFlank, rf0 = aMax - ca1 + DomeFront;
					if (rf0 < DomeFront) continue;
					float grow = 1f;
					for (int i = 0; i < nx; i++)
						for (int k = 0; k < nz; k++)
						{
							if (own[i, k] != j || float.IsNaN(cover[i, k])) continue;
							Vector3 d = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell) - m.Pos;
							float s = (Vector3.Dot(d, m.Side) - m.InsideSide - cs) / rs0, a = f * (Vector3.Dot(d, m.Along) - m.InsideAlong) - ca1;
							a /= a > 0f ? rf0 : rb0;
							float need = Mathf.Clamp01((cover[i, k] + DomeOver - baseY + DomeSink) / (tall + DomeSink));
							grow = Mathf.Max(grow, DomeU(s, a) / BellAt(need));
						}
					// (the smallest footprint; the front - the mouth's face - kept short)
					float area = grow * grow * rs0 * (rb0 + rf0);
					if (area < best) { best = area; ca = ca1; rs = rs0 * grow; rb = rb0 * grow; rf = rf0 * grow; }
				}
				if (float.IsInfinity(best)) continue;
				// (cave.dome: the footprint shrunk to that reach, its height kept - w02's dens reached 40-80 m and buried camps and
				// sheds, 11-10; the roof cells keep the hill's own slopes under it so none shows)
				float most = Mathf.Max(rs, Mathf.Max(rb, rf));
				if (DomeCapped(m) && most > m.DomeMax) { float sh = m.DomeMax / most; rs *= sh; rb *= sh; rf *= sh; }
				var u = new float[nx, nz];
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						Vector3 d = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell) - m.Pos;
						float s = (Vector3.Dot(d, m.Side) - m.InsideSide - cs) / rs, a = f * (Vector3.Dot(d, m.Along) - m.InsideAlong) - ca;
						a /= a > 0f ? rf : rb;
						u[i, k] = DomeU(s, a);
					}
				if (dome == null)
				{
					dome = new float[nx, nz];
					for (int i = 0; i < nx; i++)
						for (int k = 0; k < nz; k++) dome[i, k] = float.NaN;
				}
				for (int i = 0; i < nx; i++)
					for (int k = 0; k < nz; k++)
					{
						if (u[i, k] >= 1f) continue;
						float p = Bell(u[i, k]);
						// (its foot on the ground where that is lower than the shell's pad: the shore round it)
						float g = Mathf.Lerp(Mathf.Min(baseY, ground[i, k]), baseY, p);
						float h = g - DomeSink + (tall + DomeSink) * p;
						// (a lone den's: out past its mouth's plane no higher than the funnel ShoulderRamps makes round the arch - the
						// dome ran on out over the way in and its corridor cut a deep sheer trench up the dome's face, the dome hanging
						// over it, Shelter Atoll 11-10 r63s30; now the arch sits in the dome's face)
						if (!float.IsNaN(m.Front))
						{
							Vector3 d = new Vector3(x0 + i * Cell, 0f, z0 + k * Cell) - m.Pos;
							float past = f * (Vector3.Dot(d, m.Along) - m.InsideAlong) - (m.Plus ? startPlus[j] : startMinus[j]);
							if (past > 0f)
							{
								float sl = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, m.Side) - m.InsideSide) - (m.ArchHalf + LoneSlot));
								float cap = m.FloorY + m.ArchUp * Mathf.Clamp01(1f - past / LoneEdge) + LoneSide * sl * (1f + sl / ShoulderCurve) - LoneFall * past;
								h = Mathf.Min(h, Mathf.Max(cap, g - DomeSink));
							}
						}
						if (float.IsNaN(dome[i, k]) || h > dome[i, k]) dome[i, k] = h;
					}
				LastDomeReach = Mathf.Max(LastDomeReach, Mathf.Max(rs, Mathf.Max(rb, rf)) + Mathf.Abs(cs) + Mathf.Abs(ca));
				LastDome += m.Name + ": dome " + tall.ToString("0.0") + " m over " + baseY.ToString("0.0") + ", reach side " + rs.ToString("0.0") + " back " + rb.ToString("0.0") + " front " + rf.ToString("0.0") + "; ";
			}
			if (LastDome.Length > 0) Debug.Log("[CUSTOM ISLANDS] Cave mountain " + LastDome);
			return dome;
		}

		/// <summary>Tests: how many cells of loose skin the last build left out.</summary>
		public static int LooseDropped;

		/// <summary>Leaves out each piece of the skin (cells joined side by side) of LooseCells or fewer with no cave roof under
		/// any of it. How many cells it left out.</summary>
		static int DropLoose(bool[,] on, float[,] top, float[,] height, float[,] ground, int nx, int nz)
		{
			var seen = new bool[nx, nz];
			var piece = new List<int>();
			var todo = new Stack<int>();
			int dropped = 0;
			for (int i0 = 0; i0 < nx; i0++)
				for (int k0 = 0; k0 < nz; k0++)
				{
					if (!on[i0, k0] || seen[i0, k0]) continue;
					piece.Clear();
					bool roofed = false;
					seen[i0, k0] = true;
					todo.Push(i0 * nz + k0);
					while (todo.Count > 0)
					{
						int c = todo.Pop(), i = c / nz, k = c % nz;
						piece.Add(c);
						roofed |= !float.IsNaN(top[i, k]);
						if (i > 0 && on[i - 1, k] && !seen[i - 1, k]) { seen[i - 1, k] = true; todo.Push((i - 1) * nz + k); }
						if (i + 1 < nx && on[i + 1, k] && !seen[i + 1, k]) { seen[i + 1, k] = true; todo.Push((i + 1) * nz + k); }
						if (k > 0 && on[i, k - 1] && !seen[i, k - 1]) { seen[i, k - 1] = true; todo.Push(i * nz + k - 1); }
						if (k + 1 < nz && on[i, k + 1] && !seen[i, k + 1]) { seen[i, k + 1] = true; todo.Push(i * nz + k + 1); }
					}
					if (roofed || piece.Count > LooseCells) continue;
					foreach (int c in piece) { int i = c / nz, k = c % nz; on[i, k] = false; height[i, k] = ground[i, k] - 3.5f; }
					dropped += piece.Count;
				}
			return dropped;
		}
		internal static bool RockAsDen(MeshRenderer mr, GameObject den)
		{
			Material m = mr != null ? mr.sharedMaterial : null;
			if (m == null) return false;
			Shader sh = m.shader;
			string rockTex = null;
			var others = new List<string>();
			for (int i = 0; i < sh.GetPropertyCount(); i++)
			{
				if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
				string tp = sh.GetPropertyName(i), low = tp.ToLowerInvariant();
				if (low.Contains("normal") || low.Contains("bump") || low.Contains("mask") || low.Contains("noise") || low.Contains("height")
					|| low.Contains("occlusion") || low.Contains("metal") || low.Contains("smooth") || low.Contains("emission") || m.GetTexture(tp) == null) continue;
				if (rockTex == null && low.Contains("rock")) rockTex = tp; else others.Add(tp);
			}
			if (rockTex == null) { Debug.Log("[CUSTOM ISLANDS] Cave mountain rock as den: " + m.name + " [" + sh.name + "] has no rock texture"); return false; }
			Texture rock = m.GetTexture(rockTex);
			var keep = new MaterialPropertyBlock();
			mr.GetPropertyBlock(keep);
			var measure = new MaterialPropertyBlock();
			mr.GetPropertyBlock(measure);
			foreach (string o in others) measure.SetTexture(o, rock);
			mr.SetPropertyBlock(measure);
			Color shell, hill = Color.white;
			// (from the sides only, as it is seen from the shore: from above, the den's brown floor and rubble through its open back
			// counted, and the hill came out red-brown next to its grey-olive outer faces - 11-07)
			// (and the den only across it, from its sides: looking along it, through its mouth and its open back, its brown inside
			// counted too, and the hill still came out redder than the den's outer faces - 11-07 second look)
			Vector3 across = den.transform.right;
			across.y = 0f;
			across = across.sqrMagnitude > 0.01f ? across.normalized : Vector3.right;
			Color along;
			bool read = CaveTool.LitColour(CaveTool.DenShell(den), out shell, true, new[] { across, -across }) && CaveTool.LitColour(new Renderer[] { mr }, out hill, true);
			if (read && CaveTool.LitColour(CaveTool.DenShell(den), out along, true, new[] { Vector3.Cross(Vector3.up, across), -Vector3.Cross(Vector3.up, across) }))
				Debug.Log("[CUSTOM ISLANDS] Cave mountain rock as den: shell seen along it " + along.ToString("0.00"));
			mr.SetPropertyBlock(keep);
			if (!read) { Debug.Log("[CUSTOM ISLANDS] Cave mountain rock as den: colours not read"); return false; }
			System.Func<float, float, float> ratio = (a, b) => Mathf.Clamp(a / Mathf.Max(0.02f, b), 0.1f, 4f);
			Color factor = new Color(ratio(shell.r, hill.r), ratio(shell.g, hill.g), ratio(shell.b, hill.b), 1f);
			// (a third of the way to grey: the full factor turned Balboa's rock red-orange next to the den's olive-brown faces, the
			// same mean colour but a stronger hue - 11-07)
			float mean = (factor.r + factor.g + factor.b) / 3f;
			factor = Color.Lerp(factor, new Color(mean, mean, mean, 1f), DenRockGrey);
			Texture2D t = DigTunnel.Scaled(rock, factor, " (den rock)");
			if (t == null) return false;
			keep.SetTexture(rockTex, t);
			mr.SetPropertyBlock(keep);
			Debug.Log("[CUSTOM ISLANDS] Cave mountain rock as den: shell " + shell.ToString("0.00") + ", hill rock " + hill.ToString("0.00") + ", " + rockTex + " x " + factor.ToString("0.00"));
			return true;
		}

		/// <summary>Raft's island rock (CaravanIslandRocks) has no colour property, so ApplyTint leaves it orange under a dark
		/// den (The Abyss, Balboa Remade): its diffuse textures are swapped, on this renderer only, for copies multiplied by
		/// the tint (11-03).</summary>
		internal static void TintRockTextures(MeshRenderer mr, Color factor)
		{
			Material m = mr.sharedMaterial;
			if (m == null) return;
			var block = new MaterialPropertyBlock();
			mr.GetPropertyBlock(block);
			var done = new List<string>();
			Shader sh = m.shader;
			// (every colour texture of the shader: island rocks differ - _TopDiffuse/_SideDiffuse on CaravanIslandRocks, others on
			// Balboa's and The Abyss's rock; normal, mask and noise maps keep theirs)
			for (int i = 0; i < sh.GetPropertyCount(); i++)
			{
				if (sh.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
				string tp = sh.GetPropertyName(i), low = tp.ToLowerInvariant();
				if (low.Contains("normal") || low.Contains("bump") || low.Contains("mask") || low.Contains("noise") || low.Contains("height")
					|| low.Contains("occlusion") || low.Contains("metal") || low.Contains("smooth") || low.Contains("emission")) continue;
				Texture2D t = DigTunnel.Scaled(m.GetTexture(tp), factor, " (cave tint)");
				if (t == null) continue;
				block.SetTexture(tp, t);
				done.Add(tp);
			}
			if (done.Count > 0) mr.SetPropertyBlock(block);
			Debug.Log("[CUSTOM ISLANDS] Cave mountain rock " + m.name + " [" + sh.name + "] textures tinted: " + string.Join(",", done.ToArray()));
		}

		/// <summary>A cave piece's scale over the one raft_props measured it at (its catalog copy's); 1 when that copy isn't loaded.</summary>
		internal static Vector3 OverMeasured(Transform t, string name)
		{
			GameObject proto = name != null ? PlaceableCatalog.Get(name) : null;
			if (proto == null) return Vector3.one;
			Vector3 s = t.lossyScale, m = proto.transform.localScale;
			return new Vector3(Mathf.Abs(m.x) > 1e-4f ? s.x / m.x : 1f, Mathf.Abs(m.y) > 1e-4f ? s.y / m.y : 1f, Mathf.Abs(m.z) > 1e-4f ? s.z / m.z : 1f);
		}

		/// <summary>The mountain's look: its faces again, turned the other way (its collider keeps the one side).</summary>
		static Mesh TwoSided(Mesh one, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
		{
			int n = verts.Count;
			var v2 = new List<Vector3>(verts);
			var n2 = new List<Vector3>(norms);
			var u2 = new List<Vector2>(uvs);
			var t2 = new List<int>(tris);
			v2.AddRange(verts);
			foreach (Vector3 x in norms) n2.Add(-x);
			u2.AddRange(uvs);
			for (int i = 0; i + 2 < tris.Count; i += 3) { t2.Add(tris[i] + n); t2.Add(tris[i + 2] + n); t2.Add(tris[i + 1] + n); }
			var mesh = new Mesh { name = one.name + " (both sides)" };
			if (v2.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
			mesh.SetVertices(v2);
			mesh.SetNormals(n2);
			mesh.SetUVs(0, u2);
			mesh.SetTriangles(t2, 0);
			mesh.bounds = one.bounds;
			return mesh;
		}

		/// <summary>The biggest rock or boulder placed on this island (not a cave piece), or null.</summary>
		/// <summary>A dug tunnel's rim walls (its rock's part 1, round the mouth out in the open) wear the cave hill's skin as it is drawn -
		/// its material and its tint - not Raft's island rock, which went blue-grey in the shade beside the hill's sand-brown rock
		/// (Ironreef Caverns' mouth C, 11-05). No hill built yet: the rock it will wear (the island's biggest, else Raft's big rock);
		/// the hill dresses its tunnels again once it is built. Returns the material's name, null when it left them.</summary>
		/// <summary>Tests: the skin's renderer (null before a build).</summary>
		public MeshRenderer SkinRenderer { get { return skin != null ? skin.GetComponent<MeshRenderer>() : null; } }

		static readonly HashSet<string> dumped = new HashSet<string>();
		public static string DressRimWalls(MeshRenderer mr)
		{
			if (mr == null) return null;
			Material[] mats = mr.sharedMaterials;
			if (mats.Length < 2) return null;
			CaveMountain hill = mr.GetComponentInParent<CaveMountain>();
			MeshRenderer skinLook = hill != null && hill.skin != null ? hill.skin.GetComponent<MeshRenderer>() : null;
			Material m = skinLook != null ? skinLook.sharedMaterial : null;
			var block = new MaterialPropertyBlock();
			if (m != null) skinLook.GetPropertyBlock(block);
			else
			{
				Renderer rock = hill != null ? hill.IslandRock() : null;
				if (rock == null)
				{
					GameObject big = PlaceableCatalog.Get("BigRock_1");
					rock = big != null ? big.GetComponentInChildren<MeshRenderer>(true) : null;
				}
				m = rock != null ? rock.sharedMaterial : null;
			}
			if (m == null) return null;
			mats[1] = m;
			mr.sharedMaterials = mats;
			mr.SetPropertyBlock(block, 1);
			DigTunnel.SideRockOnly(mr, 1);
			if (dumped.Add(m.name))
			{
				var sb = new System.Text.StringBuilder("[CUSTOM ISLANDS] Rim walls' rock " + m.name + " (" + (m.shader != null ? m.shader.name : "?") + "):");
				Shader sh = m.shader;
				for (int p = 0; sh != null && p < sh.GetPropertyCount(); p++)
				{
					string pn = sh.GetPropertyName(p);
					var pt = sh.GetPropertyType(p);
					if (pt == UnityEngine.Rendering.ShaderPropertyType.Texture) { Texture tx = m.GetTexture(pn); sb.Append(" " + pn + "=" + (tx != null ? tx.name : "-") + (block.isEmpty || block.GetTexture(pn) == null ? "" : "/blk " + block.GetTexture(pn).name)); }
					else if (pt == UnityEngine.Rendering.ShaderPropertyType.Float || pt == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append(" " + pn + "=" + m.GetFloat(pn).ToString("0.##"));
				}
				Debug.Log(sb.ToString());
			}
			return m.name;
		}

		/// <summary>The skin's material and tint onto every dug tunnel's rim walls under this island (DressRimWalls).</summary>
		void DressTunnels()
		{
			foreach (DigTunnel t in GetComponentsInChildren<DigTunnel>(true))
			{
				Transform r = t.transform.Find(DigTunnel.RockName);
				MeshRenderer mr = r != null ? r.GetComponent<MeshRenderer>() : null;
				if (mr != null) t.RimWalls = DressRimWalls(mr) ?? "";
			}
		}

		Renderer IslandRock()
		{
			Renderer best = null;
			float bestSize = 0f;
			foreach (Transform o in transform)
			{
				if (o == null || caves.Contains(o) || (skin != null && o == skin.transform)) continue;
				string n = o.name.ToLowerInvariant();
				if (!(n.Contains("rock") || n.Contains("boulder") || n.Contains("cliff")) || n.Contains("cave")) continue;
				foreach (Renderer r in o.GetComponentsInChildren<MeshRenderer>())
				{
					if (!r.enabled || r.sharedMaterial == null) continue;
					Vector3 z = r.bounds.size;
					float size = z.x * z.y * z.z;
					if (size > bestSize) { bestSize = size; best = r; }
				}
			}
			return best;
		}

		/// <summary>
		/// How far from a cave's inside point its roof goes along the passage (dir 1: its plus end, -1: its minus end): the last
		/// roof on the passage's middle or either side of it, past gaps in the roof of up to 5 m.
		/// </summary>
		/// <summary>Which pieces are one passage: joined end to end (an open end within 2.5 m of another's, floors within 3 m) - the
		/// cave tool's tunnels and its den. Each piece's chain number.</summary>
		static int[] Chains(List<Mouth> mouths)
		{
			int n = mouths.Count;
			var chain = new int[n];
			for (int j = 0; j < n; j++) chain[j] = j;
			System.Func<int, int> root = null;
			root = j => chain[j] == j ? j : (chain[j] = root(chain[j]));
			System.Func<Mouth, float, Vector3> end = (m, dir) => m.Pos + m.Along * (m.InsideAlong + dir * (dir > 0f ? m.OwnPlus : m.OwnMinus)) + m.Side * m.InsideSide;
			for (int j = 0; j < n; j++)
				for (int l = j + 1; l < n; l++)
				{
					if (Mathf.Abs(mouths[j].FloorY - mouths[l].FloorY) > 3f) continue;
					bool joined = false;
					foreach (float d1 in new[] { 1f, -1f })
						foreach (float d2 in new[] { 1f, -1f })
						{
							Vector3 e = end(mouths[j], d1) - end(mouths[l], d2);
							if (new Vector2(e.x, e.z).magnitude < 2.5f) joined = true;
						}
					if (joined) chain[root(j)] = root(l);
				}
			for (int j = 0; j < n; j++) chain[j] = root(j);
			return chain;
		}

		/// <summary>A roof cell of the piece's own chain (or one no piece claims).</summary>
		static bool OwnRoof(int[,] own, int[] chain, int j, int i, int k)
		{
			int o = own[i, k];
			return o < 0 || o >= chain.Length || chain[o] == chain[j];
		}

		/// <summary>How far out a way in may run (m along the passage past the inside point): to the first roof of another cave
		/// past where its own roof ends - the way in doesn't go on through another cave's hill and out the far side.</summary>
		static float WayStop(Mouth m, float dir, float start, float[,] top, int[,] own, int[] chain, int j, float x0, float z0, int nx, int nz)
		{
			for (float a = start; a < 80f; a += Cell * 0.5f)
			{
				Vector3 p = m.Pos + m.Along * (m.InsideAlong + dir * a) + m.Side * m.InsideSide;
				int i = Mathf.RoundToInt((p.x - x0) / Cell), k = Mathf.RoundToInt((p.z - z0) / Cell);
				if (i < 0 || k < 0 || i >= nx || k >= nz) break;
				if (!float.IsNaN(top[i, k]) && !OwnRoof(own, chain, j, i, k)) return a - start < 5f ? start : a; // (another cave right on: no way in between, as the roof run bridged 5 m before)
			}
			return float.PositiveInfinity;
		}

		static float RoofEnd(Mouth m, float dir, float[,] top, int[,] own, int[] chain, int j, float x0, float z0, int nx, int nz)
		{
			float last = 0f;
			foreach (float s in new[] { 0f, -m.HalfWidth * 0.5f, m.HalfWidth * 0.5f })
			{
				float seen = 0f;
				for (float a = 0f; a < 80f; a += Cell * 0.5f)
				{
					Vector3 p = m.Pos + m.Along * (m.InsideAlong + dir * a) + m.Side * (m.InsideSide + s);
					int i = Mathf.RoundToInt((p.x - x0) / Cell), k = Mathf.RoundToInt((p.z - z0) / Cell);
					if (i < 0 || k < 0 || i >= nx || k >= nz) break;
					if (!float.IsNaN(top[i, k]) && !OwnRoof(own, chain, j, i, k)) break;
					if (!float.IsNaN(top[i, k])) seen = a;
					else if (a - seen > 5f) break;
				}
				last = Mathf.Max(last, seen);
			}
			return last;
		}

		/// <summary>The edge (i1,k1)-(i2,k2) of a surface square; (di,dk) points to the square beside it.</summary>
		static void Edge(int i1, int k1, int i2, int k2, int di, int dk, int nx, int nz, bool[,] on, float[,] top, float[,] height,
			float[,] ground, float[,] corridor, float[,] mouthAt, float[,] floorAt, System.Func<int, int, Vector3> P, List<Vector3> verts,
			List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 origin, Arch arch, Vector3[,] gn)
		{
			// (its faces lit as the hill's surface at the edge, both sides: lit by their own side, the ones facing away from the light
			// stood out as dark squares on the arch's pillars, 11-08 r26-r38 user)
			BandLit = (gn[i1, k1] + gn[i2, k2]).normalized;
			int a1 = i1 + di, c1 = k1 + dk, a2 = i2 + di, c2 = k2 + dk;
			bool inside1 = a1 >= 0 && c1 >= 0 && a1 < nx && c1 < nz, inside2 = a2 >= 0 && c2 >= 0 && a2 < nx && c2 < nz;
			if (inside1 && inside2 && on[a1, c1] && on[a2, c2]) return; // the surface goes on
			Vector3 n = new Vector3(di, 0f, dk);
			float g1 = ground[i1, k1] - 0.3f, g2 = ground[i2, k2] - 0.3f;
			float way = float.NaN;
			if (inside1 && !float.IsNaN(corridor[a1, c1])) way = corridor[a1, c1];
			if (inside2 && !float.IsNaN(corridor[a2, c2])) way = float.IsNaN(way) ? corridor[a2, c2] : Mathf.Min(way, corridor[a2, c2]);
			Vector3 p1 = P(i1, k1), p2 = P(i2, k2);
			bool atWay = !float.IsNaN(way) && (!float.IsNaN(top[i1, k1]) || !float.IsNaN(top[i2, k2]) || !float.IsNaN(mouthAt[i1, k1]) || !float.IsNaN(mouthAt[i2, k2]));
			if (atWay)
			{
				// The open end of a cave: its own arch is the face; under its floor, down to the ground (no higher than the
				// floor there: a lip over it stopped the way in)
				float f = Mathf.Max(float.IsNaN(floorAt[i1, k1]) ? float.NegativeInfinity : floorAt[i1, k1], float.IsNaN(floorAt[i2, k2]) ? float.NegativeInfinity : floorAt[i2, k2]);
				if (f > way - 3f && f < way) way = f;
			}
			// (beside the way in, over the cave's own roof, its arch goes on: the face there is the cave's too - a wall down to the
			// ground stood just inside a mouth wider than the way in, Deepwatch Station's tunnel)
			bool dry = atWay && (arch.DryAt(i1, k1) || arch.DryAt(i2, k2));
			float floorY = atWay ? way : arch.FaceOut(i1, k1, i2, k2, a1, c1, a2, c2, inside1, inside2, n);
			if (float.IsNaN(floorY))
			{
				if (p1.y <= g1 && p2.y <= g2) return;
				Band(p1, p2, Mathf.Min(g1, p1.y), Mathf.Min(g2, p2.y), p1.y, p2.y, n, verts, norms, uvs, tris, origin);
				return;
			}
			// In strips along it: where the cave's passage is, open from its floor to its roof (rock from the hill down to its
			// roof, and from its floor down to the ground); elsewhere a skirt under the way in, or the wall down to the ground.
			// (a skirt up to the passage's floor further in, and a wall at the way in's side, boxed in the mouth of a tunnel
			// that widens and dips at its ends - Deepwatch Station)
			float lo0, hi0, lo, hi;
			// (cave.foot=dry: the passage looked for half a square back, under the arch: at the face's line, just out past it, none
			// was found and the face over the way in came down to head height)
			Vector3 under = dry ? -n * (Cell * 0.5f) : Vector3.zero;
			bool open0 = arch.Passage(p1 + under, floorY, out lo0, out hi0);
			for (int s = 1; s <= Arch.Strips; s++)
			{
				float t0 = (s - 1) / (float)Arch.Strips, t = s / (float)Arch.Strips;
				Vector3 e0 = Vector3.Lerp(p1, p2, t0), e = Vector3.Lerp(p1, p2, t);
				float ga = Mathf.Lerp(g1, g2, t0), gb = Mathf.Lerp(g1, g2, t);
				bool open = arch.Passage(e + under, floorY, out lo, out hi);
				if (open || open0)
				{
					float l = open && open0 ? Mathf.Min(lo, lo0) : open ? lo : lo0, h = open && open0 ? Mathf.Max(hi, hi0) : open ? hi : hi0;
					// (at the way in itself no higher than its floor, and nothing over it - its own arch is the face there: the floor
					// and roof found in a bear cave's mouth stood 0.5-1.2 m into the way in, a step and a lintel across it on 15
					// islands' dens; only a mouth that dips under the way in's floor takes the skirt down with it)
					if (atWay) l = Mathf.Min(l, way);
					// (beside a way in out in front of its arch, no higher than the skin's own edge: the skirt rose to the passage's floor
					// over the sand there, a dark straight wall beside the raised sand, Caisson Yard and Shelter Atoll 11-09 r53-r55)
					else if (!float.IsNaN(way)) l = Mathf.Min(l, Mathf.Max(e0.y, e.y));
					Band(e0, e, ga, gb, l, l, n, verts, norms, uvs, tris, origin);
					// (cave.foot=dry: over its own arch too, up to the dome - the skin's edge stood over the arch with nothing under it,
					// a pale see-through sheet over the ammo den's mouth, Shelter Atoll 11-08)
					if (!atWay || dry) Band(e0, e, h, h, e0.y, e.y, n, verts, norms, uvs, tris, origin);
				}
				else if (atWay)
				{
					if (Mathf.Min(ga, gb) <= way - 0.15f) Band(e0, e, Mathf.Min(ga, way), Mathf.Min(gb, way), way, way, n, verts, norms, uvs, tris, origin);
					// (cave.foot=dry: beside the opening, rock from head height over the way in up to the dome)
					if (dry && Mathf.Min(e0.y, e.y) > way + DryHead) Band(e0, e, way + DryHead, way + DryHead, e0.y, e.y, n, verts, norms, uvs, tris, origin);
				}
				else Band(e0, e, Mathf.Min(ga, e0.y), Mathf.Min(gb, e.y), e0.y, e.y, n, verts, norms, uvs, tris, origin);
				open0 = open; lo0 = lo; hi0 = hi;
			}
		}

		/// <summary>A face up the edge a-b (x, z), from ya0/yb0 to ya1/yb1; none where it has no height.</summary>
		static void Band(Vector3 a, Vector3 b, float ya0, float yb0, float ya1, float yb1, Vector3 n, List<Vector3> verts,
			List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 origin)
		{
			if (ya1 <= ya0 + 0.01f && yb1 <= yb0 + 0.01f) return;
			ya0 = Mathf.Min(ya0, ya1); yb0 = Mathf.Min(yb0, yb1);
			// (each side lit as facing a little up, both sides alike: lit by its flat side normal, the side seen from out of the
			// hole faced away from the light - dark square patches on the arch's pillars, Shelter Atoll, Balboa Remade, Canopy
			// Stair, 11-08 r26/r31 user)
			Vector3 p0 = new Vector3(a.x, ya0, a.z), p1 = new Vector3(a.x, ya1, a.z), p2 = new Vector3(b.x, yb1, b.z), p3 = new Vector3(b.x, yb0, b.z);
			Vector3 fr = (n + Vector3.up * WallLift).normalized, bk = (-n + Vector3.up * WallLift).normalized;
			// (the rock's shader draws both sides, a back side lit by its normal turned round: the two faces at one place took turns
			// in the depth test, one lit up and one turned down - the dark squares on the arch's pillars, 11-08 r26-r39 user; so the
			// back face's normal is exactly the front's turned round, and both lit as the hill's surface at the edge)
			if (BandLit.sqrMagnitude > 0.5f) fr = BandLit;
			bk = -fr;
			Quad(verts, norms, uvs, tris, origin, p0, p1, p2, p3, n, false, new[] { fr, fr, fr, fr });
			Quad(verts, norms, uvs, tris, origin, p3, p2, p1, p0, -n, false, new[] { bk, bk, bk, bk });
		}
		const float UvKeep = 0.35f; // (Quad: how much a triangle must face the side its corners' normals pick to be mapped from it)
		[System.ThreadStatic] static Vector3 BandLit; // (Edge: the hill's surface normal at the edge its Bands are under)
		const float WallLift = 0.3f; // (more turns it to sand: the rock shader blends by the normal)

		/// <summary>The sea's surface (world y) and how far under it the ground must be for the hill not to go out over it; the shore cells
		/// next to those, up to SeaShoreBy over it, go too.</summary>
		const float SeaLevel = 0f, SeaFloorBy = 1f, SeaShoreBy = 0.5f;
		/// <summary>A surface the rounding lifted to within SeaLidBy under the water line, standing more than SeaGap over the sea floor, is a lid
		/// over the water and goes as well (round 4).</summary>
		const float SeaLidBy = 2f, SeaGap = 1f;

		/// <summary>Whether a bare cell (no roof or low edge of its own) goes for the sea: its ground well under the water and the hill over it
		/// either above the water (raw: before the rounding) or lifted by the rounding to a lid near the water line over a gap of sea.</summary>
		public static bool SeaCut(float raw, float height, float ground)
		{
			if (!(ground < SeaLevel - SeaFloorBy)) return false;
			return raw > SeaLevel || height > SeaLevel - SeaLidBy && height > ground + SeaGap;
		}

		/// <summary>The caves' open ends where the hill's faces cross them.</summary>
		sealed class Arch
		{
			public const int Strips = 8; // (per edge of a square: 0.16 m)
			static readonly float[] Probe = { 0.6f, 1.6f, 2.6f };
			public HashSet<Collider> Shells;
			public Bounds Box;
			public int[,] Own;
			public float[][,] Ahead;
			public List<Mouth> Mouths;
			public float[,] Top;

			/// <summary>The floor of the cave whose open end the face crosses, over that cave's own roof (NaN: not one).</summary>
			public float FaceOut(int i1, int k1, int i2, int k2, int a1, int c1, int a2, int c2, bool in1, bool in2, Vector3 n)
			{
				int j = !float.IsNaN(Top[i1, k1]) ? Own[i1, k1] : !float.IsNaN(Top[i2, k2]) ? Own[i2, k2] : -1;
				if (j < 0 || j >= Mouths.Count) return float.NaN;
				if (!((in1 && Ahead[j][a1, c1] > 0f) || (in2 && Ahead[j][a2, c2] > 0f))) return float.NaN;
				return Mathf.Abs(Vector3.Dot(n, Mouths[j].Along)) < 0.5f ? float.NaN : Mouths[j].FloorY;
			}

			/// <summary>Whether the cell's roof or low edge is a cave.foot=dry piece's.</summary>
			public bool DryAt(int i, int k) { int j = Own[i, k]; return j >= 0 && j < Mouths.Count && Mouths[j].Dry; }

			/// <summary>Whether the spot (x, z) is in a cave's passage just over that floor: from its floor (lo) to its roof (hi).</summary>
			public bool Passage(Vector3 at, float floorY, out float lo, out float hi)
			{
				lo = hi = 0f;
				foreach (float up in Probe)
				{
					Vector3 p = new Vector3(at.x, floorY + up, at.z);
					float under, over;
					if (p.y <= Box.min.y || p.y >= Box.max.y || !CaveGround.Inside(p, Shells, Box, out under, out over)) continue;
					lo = p.y - under; hi = p.y + over;
					return true;
				}
				return false;
			}
		}

		/// <summary>A four-sided face a-b-c-d with normal n (the other side too when both).</summary>
		static void Quad(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 origin,
			Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, bool both, Vector3[] smooth = null)
		{
			// (projected along each triangle's own plane, not the given normal nor the square's: a steep face whose given normal
			// pointed elsewhere took a projection along itself and showed stripes - Shelter den's cliff beside the mouth, 11-08 -
			// and a twisted square on a steep slope, one projection for both its halves, stretched one half into a vertical smear
			// of stair-stepped stripes on the arch's pillars, 11-08 r26 user)
			Vector3[] corner = { a, b, c, d };
			int[][] halves = { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } };
			for (int side = 0; side < (both ? 2 : 1); side++)
			{
				Vector3 nn = side == 0 ? n : -n;
				// (a-b-c-d winds so that n is its front; the back side winds the other way)
				Vector3 f = Vector3.Cross(b - a, c - a);
				bool front = Vector3.Dot(f, nn) > 0f;
				foreach (int[] h in halves)
				{
					int s = verts.Count;
					Vector3 pn = Vector3.Cross(corner[h[1]] - corner[h[0]], corner[h[2]] - corner[h[0]]);
					if (pn.sqrMagnitude < 1e-8f) pn = Vector3.Cross(c - a, d - b);
					if (pn.sqrMagnitude < 1e-8f) pn = n;
					float ax = Mathf.Abs(pn.x), ay = Mathf.Abs(pn.y), az = Mathf.Abs(pn.z);
					// (the side from the corners' smooth normals, the same for the square's halves and mostly for its neighbours: by
					// each triangle's own plane, the rough rock's triangles side by side took different sides - square patches of
					// the rock's texture cut off on the arch's pillars, 11-08 r26-r34 user; its own plane still where that side
					// would stretch this triangle, 11-08 r26)
					if (smooth != null)
					{
						// (the corners' sides in order, the next one before the triangle's own plane: a falling back to its own
						// plane still left square patches, 11-08 r36)
						Vector3 sm = smooth[0] + smooth[1] + smooth[2] + smooth[3];
						float[] bs = { Mathf.Abs(sm.x), Mathf.Abs(sm.y), Mathf.Abs(sm.z) }, o = { ax, ay, az };
						float len = pn.magnitude;
						for (int pick = 0; pick < 2; pick++)
						{
							int best = -1;
							for (int k = 0; k < 3; k++) if (bs[k] >= 0f && (best < 0 || bs[k] > bs[best])) best = k;
							if (best < 0 || bs[best] <= 0f) break;
							if (o[best] > UvKeep * len) { ax = best == 0 ? 1f : 0f; ay = best == 1 ? 1f : 0f; az = best == 2 ? 1f : 0f; break; }
							bs[best] = -1f;
						}
					}
					foreach (int j in h)
					{
						Vector3 v = corner[j];
						verts.Add(v - origin);
						// (smooth: each corner's own, shared with the squares round it - see the top's)
						norms.Add(smooth != null ? (side == 0 ? smooth[j] : -smooth[j]) : nn);
						// (mapped from the side it faces most, in world metres: the rock's texture lines up across squares)
						uvs.Add(ay >= ax && ay >= az ? new Vector2(v.x, v.z) / 14f : ax >= az ? new Vector2(v.z, v.y) / 14f : new Vector2(v.x, v.y) / 14f);
					}
					if (front) { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); }
					else { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); }
				}
			}
		}
	}
}
