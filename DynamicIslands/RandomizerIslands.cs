using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A look borrowed from one of Raft's quest islands, for dressing a spot of a randomized island: a centrepiece, props
	/// around it and small things between (all measured in raft_props.txt), a container and a note.
	/// </summary>
	public class Theme
	{
		public string Name, Label;
		/// <summary>Island styles it suits (TerrainPainter styles).</summary>
		public int[] Styles;
		public string[] Anchors, Medium, Small;
		public string Loot, Container, NoteKind, NoteTitle;
		public string[] Notes;
		/// <summary>Animals that fit it (a spot near the scene now and then), or null.</summary>
		public string Creature;
	}

	/// <summary>
	/// The world randomizer's bigger pieces: themed scenes from the quest islands' props, caves (Raft's own cave pieces:
	/// set into a hill on generated islands, or under a mound of rocks on Raft's islands, whose ground can't be dug), and
	/// the large islands made like Raft's big ones.
	/// </summary>
	public static class RandomizerIslands
	{
		const int Tropical = TerrainPainter.Tropical, Snowy = TerrainPainter.Snowy, Desert = TerrainPainter.Desert, Forest = TerrainPainter.Forest, Volcanic = TerrainPainter.Volcanic;

		public static readonly Theme[] Themes =
		{
			new Theme { Name = "scrapyard", Label = "Scrapyard", Styles = new[] { Tropical, Desert, Forest, Volcanic },
				Anchors = new[] { "VP_Excavator", "VP_Forklift", "VP_Dumpster01" },
				Medium = new[] { "VP_ConcretePipe01", "VP_ConcretePipe02", "VP_Dumpster02", "VP_BrickStack01", "VP_BrickStack02", "VP_BrickStack03", "VP_ExplosiveBarrel_Pile", "Pallet" },
				Small = new[] { "VP_MetalCrate01", "VP_MetalCrate02", "VP_MetalCrate03", "VP_Cable_Roll01", "Tire_02", "Tire_03", "VP_FoldingLadder_Tall" },
				Loot = "Metal", Container = "Loot_Crate", NoteKind = "Note_Papers", NoteTitle = "Site log",
				Notes = new[] { "Work stopped when the water came. The machines stayed.", "Whoever finds the key to the digger: it doesn't matter any more. Take the scrap.", "Shift report: flooded. Next shift: cancelled." } },
			new Theme { Name = "market", Label = "Old market", Styles = new[] { Tropical, Desert },
				Anchors = new[] { "UT_ClothOverhangBig04", "UT_ClothOverhangBig05" },
				Medium = new[] { "UT_MarketStackBox01", "Tangaroa_OutdoorFurnitureSilver_Table", "UT_CoveredCrate01", "UT_CoveredCrate02", "UT_Generator01" },
				Small = new[] { "UT_MarketBasket01", "UT_MarketBasket02", "UT_MarketBasket03", "UT_MarketCrateSmall01", "UT_MarketCrateSmall02", "Tangaroa_OutdoorFurnitureSilver_Chair", "UT_LightBottle01", "UT_DreamCatcher01", "UT_DreamCatcher02" },
				Loot = "Food", Container = "Loot_Box", NoteKind = "Note_Sign", NoteTitle = "Market sign",
				Notes = new[] { "Fresh fruit! Fair prices! (Closed until further notice.)", "Trade here: fish for rope, rope for planks.", "The market moves with the tide. Back next season." } },
			new Theme { Name = "caravan", Label = "Caravan outpost", Styles = new[] { Tropical, Desert, Volcanic },
				Anchors = new[] { "Caravan_Blue_01", "Caravan_Green_01", "Caravan_Yellow_01", "Scaffolding_6x4m" },
				Medium = new[] { "BenchTable_01", "Banner_01", "PulauKafilahFlag", "Crate_Big_01", "Crate_Big_02", "FishingNets_02", "MetalSheet_2", "MetalSheet_3", "Bench_01" },
				Small = new[] { "PlasticChair_01", "Cableroll_01", "Cableroll_02", "Crate_Small_02", "Tire_02", "Candle_01", "RopeFence_Short", "MetalTable_01" },
				Loot = "Basics", Container = "Loot_Barrel", NoteKind = "Note_Board", NoteTitle = "Outpost notice",
				Notes = new[] { "Caravan three went on to the east. Water in the barrel, use it well.", "Outpost rules: share the catch, mend the nets, don't feed the gulls.", "We'll be back when the storms settle." } },
			new Theme { Name = "bear", Label = "Bear country", Styles = new[] { Forest, Tropical, Snowy },
				Anchors = new[] { "Balboa_DecorationPrefabBase_SimpleTent", "Balboa_DecorationPrefabBase_OldCouch" },
				Medium = new[] { "Balboa_DecorationPrefabBase_Fence_Mid", "Balboa_DecorationPrefabBase_Fence_EndL", "Balboa_DecorationPrefabBase_Fence_EndBroken", "Balboa_DecorationPrefabBase_Old Stove",
					"Balboa_DecorationPrefabBase_Wooden Spikes", "Balboa_DecorationPrefabBase_Generator", "Antenna_dish", "Balboa_DecorationPrefabBase_Table" },
				Small = new[] { "BearSign1", "BearSign2 Variant", "Firewood_1", "Balboa_DecorationPrefabBase_Lantern", "Balboa_DecorationPrefabBase_Trashcan", "Balboa_DecorationPrefabBase_ToxicBarrel", "Balboa_DecorationPrefabBase_Chair", "Balboa_DirectionSign" },
				Loot = "Food", Container = "Loot_Chest", NoteKind = "Note_Sign", NoteTitle = "Warning", Creature = "Bear",
				Notes = new[] { "BEWARE OF THE BEAR. Seriously.", "Do not leave food out. It will find it. It always finds it.", "Camp moved inland. The bear did not." } },
			new Theme { Name = "frozen", Label = "Frozen camp", Styles = new[] { Snowy },
				Anchors = new[] { "TP_Igloo_Large_1", "TP_Igloo_Medium_1", "SnowmobileShedMesh" },
				Medium = new[] { "TP_Igloo_Small", "TP_Igloo_Small_1", "TP_FoldingTable_Large", "TP_IcePillar01", "TP_IcePillar02" },
				Small = new[] { "TP_FoldingStool", "TP_Moontown_Barrel02", "TP_Moontown_SealedCrate02_Clean", "TP_Moontown_TarpCrate02_Clean" },
				Loot = "Treasure", Container = "Loot_Crate", NoteKind = "Note_Paper", NoteTitle = "Expedition note",
				Notes = new[] { "Day 9 of the survey. Minus twenty. The igloos hold.", "Supplies in the crate. Keep it closed, the foxes are clever.", "We are going back. Tell Olof we tried." } },
			new Theme { Name = "garden", Label = "Hotel garden", Styles = new[] { Tropical, Forest },
				Anchors = new[] { "RestaSunshadeGround", "TangaroaFounderStatue" },
				Medium = new[] { "LargePlant1", "LargePlant2", "Table_Common_01", "RestaFence" },
				Small = new[] { "MediumPlant1", "MediumPlant2", "SmallPlant1", "Flowerpot_01", "Flowerpot_02", "Flowerpot_03", "Dinnerchair_01" },
				Loot = "Treasure", Container = "Loot_ChestSmall", NoteKind = "Note_Sign", NoteTitle = "Reserved",
				Notes = new[] { "Reserved for guests of the Tangaroa. Please do not feed the sharks.", "Garden party postponed (sea level).", "Complimentary drinks at sunset. Bring your own cup." } },
			new Theme { Name = "radio", Label = "Radio outpost", Styles = new[] { Tropical, Snowy, Forest, Volcanic, Desert },
				Anchors = new[] { "RT_WindMill", "RT_SatteliteDisc" },
				Medium = new[] { "RT_SharkCage", "RT_PowerBox", "RT_Fence" },
				Small = new[] { "RT_Signs", "RT_Spotlight", "RT_Floodlight_WithoutLightSource" },
				Loot = "Metal", Container = "Loot_Box", NoteKind = "Note_Papers", NoteTitle = "Radio log",
				Notes = new[] { "...repeat, is anyone receiving? Over.", "Signal strongest from the north-east. Follow it.", "Battery low. Leaving the spare circuit boards in the box." } },
			new Theme { Name = "raftcamp", Label = "Castaways' camp", Styles = new[] { Tropical, Forest, Desert },
				Anchors = new[] { "campfire_1" },
				Medium = new[] { "Bed_Simple", "Scarecrow", "Placeable_Lantern_FireBasket" },
				Small = new[] { "Bird_Nest", "buoy", "Firewood_1" },
				Loot = "Basics", Container = "Loot_Barrel", NoteKind = "Note_Paper", NoteTitle = "Castaway's note",
				Notes = new[] { "We pulled our raft up here to fix it. It didn't want to be fixed.", "The scarecrow keeps the gulls off. Mostly.", "Two weeks on this beach. Tomorrow we sail." } },
		};

		public static Theme ThemeOf(string name) { return Themes.FirstOrDefault(t => t.Name == name); }

		/// <summary>The themes that suit a style and whose props this Raft has (all of them when nothing was measured).</summary>
		public static List<Theme> ThemesFor(int style)
		{
			return Themes.Where(t => t.Styles.Contains(style) && t.Anchors.Any(RaftProps.Has)).ToList();
		}

		static T One<T>(System.Random r, IList<T> list) { return list[r.Next(list.Count)]; }
		static float Yaw(System.Random r) { return (float)r.NextDouble() * 360f; }
		static Vector2 Around(Vector2 c, float yaw, float x, float z) { Vector3 v = Quaternion.Euler(0f, yaw, 0f) * new Vector3(x, 0f, z); return c + new Vector2(v.x, v.z); }

		/// <summary>Stands a measured prop on generated ground (sunk a little), protected from clearing like a set piece.</summary>
		static IslandObject Prop(MapKit k, string name, Vector2 p, float yaw, float sink = 0.05f)
		{
			IslandObject o = k.Add(name, k.At(p, RaftProps.Lift(name) - sink), yaw, new Dictionary<string, string> { { "set.piece", "1" } }, 0f);
			return o;
		}

		#region Themed scenes

		/// <summary>
		/// A themed scene on generated land around c: the centrepiece, props around it facing in, small things between,
		/// the theme's container and note. Nature is cleared from the spot. Returns false if the theme has no props here.
		/// </summary>
		public static bool Dress(MapKit k, Theme t, Vector2 c, float radius, bool withNote = true)
		{
			System.Random r = k.Rnd;
			string[] anchors = t.Anchors.Where(RaftProps.Has).ToArray(), medium = t.Medium.Where(RaftProps.Has).ToArray(), small = t.Small.Where(RaftProps.Has).ToArray();
			if (anchors.Length == 0) return false;
			k.Clear(c, radius + 1f);
			float yaw = Yaw(r);
			string anchor = One(r, anchors);
			Prop(k, anchor, c, yaw);
			float anchorSize = Mathf.Max(2f, RaftProps.Get(anchor).Size.x * 0.5f, RaftProps.Get(anchor).Size.z * 0.5f);
			int nm = medium.Length == 0 ? 0 : 2 + r.Next(3);
			for (int i = 0; i < nm; i++)
			{
				float a = yaw + 60f + i * (240f / Mathf.Max(1, nm)) + (float)r.NextDouble() * 30f;
				Vector2 p = Around(c, a, 0f, anchorSize + 2f + (float)r.NextDouble() * Mathf.Max(1f, radius - anchorSize - 3f));
				if (k.Slope(p) > 22f) continue;
				Vector2 toC = c - p;
				Prop(k, One(r, medium), p, Mathf.Atan2(toC.x, toC.y) * Mathf.Rad2Deg + (float)r.NextDouble() * 30f - 15f);
			}
			int ns = small.Length == 0 ? 0 : 3 + r.Next(4);
			for (int i = 0; i < ns; i++)
			{
				Vector2 p = Around(c, Yaw(r), 0f, anchorSize + 1f + (float)r.NextDouble() * Mathf.Max(1f, radius - anchorSize - 1f));
				if (k.Slope(p) > 25f) continue;
				Prop(k, One(r, small), p, Yaw(r));
			}
			Vector2 box = Around(c, yaw + 180f, 0f, anchorSize + 1.5f);
			k.Chest(t.Container, box, t.Label, MapKit.Loot(t.Loot));
			if (withNote) k.Note(t.NoteKind, Around(c, yaw + 150f, 0f, anchorSize + 2.5f), t.NoteTitle, One(r, t.Notes));
			if (t.Creature != null && r.NextDouble() < 0.5)
			{
				Vector2? den = k.Find(c, radius * 2.5f, MapKit.Dry, 6f);
				if (den.HasValue) k.Creature(t.Creature, den.Value, 1);
			}
			return true;
		}

		/// <summary>A few of a theme's props scattered between inner and outer metres around c (no centrepiece, chest or note): a touch of it.</summary>
		public static int Sprinkle(MapKit k, Theme t, Vector2 c, float inner, float outer, int count)
		{
			System.Random r = k.Rnd;
			string[] props = t.Medium.Concat(t.Small).Where(RaftProps.Has).ToArray();
			int placed = 0;
			for (int i = 0; i < count * 4 && placed < count && props.Length > 0; i++)
			{
				Vector2 p = Around(c, Yaw(r), 0f, inner + (float)r.NextDouble() * (outer - inner));
				if (k.Ground(p) - k.Sea < 0.8f || k.Slope(p) > 22f) continue;
				k.Clear(p, 1.5f);
				Prop(k, One(r, props), p, Yaw(r));
				placed++;
			}
			return placed;
		}

		/// <summary>The theme that goes with each oddity's set piece.</summary>
		public static readonly Dictionary<string, string> OddityTheme = new Dictionary<string, string>
		{
			{ "van", "scrapyard" }, { "caravan", "caravan" }, { "planecrash", "radio" }, { "boatwreck", "raftcamp" }, { "shack", "bear" }, { "statue", "garden" }, { "rocket", "scrapyard" }, { "hut", "raftcamp" },
		};

		/// <summary>The same on one of Raft's islands (the randomizer's extras): props stood on Raft's ground found by rays.</summary>
		public static bool DressWorld(WorldKit k, LandGround g, Theme t, Vector3 c, float radius, System.Random r)
		{
			string[] anchors = t.Anchors.Where(RaftProps.Has).ToArray(), medium = t.Medium.Where(RaftProps.Has).ToArray(), small = t.Small.Where(RaftProps.Has).ToArray();
			if (anchors.Length == 0) return false;
			Func<Vector3, float, float, string, IslandObject> put = (at, yaw, sink, name) =>
			{
				Vector3 hit, n;
				if (!g.Hit(at.x, at.z, out hit, out n) || hit.y < 0.8f || Vector3.Angle(n, Vector3.up) > 25f) return null;
				return k.Add(name, hit + Vector3.up * (RaftProps.Lift(name) - sink), yaw, new Dictionary<string, string> { { "set.piece", "1" } });
			};
			float y0 = Yaw(r);
			string anchor = One(r, anchors);
			if (put(c, y0, 0.05f, anchor) == null) return false;
			float anchorSize = Mathf.Max(2f, RaftProps.Get(anchor).Size.x * 0.5f, RaftProps.Get(anchor).Size.z * 0.5f);
			int nm = medium.Length == 0 ? 0 : 2 + r.Next(3);
			for (int i = 0; i < nm; i++)
			{
				float a = y0 + 60f + i * (240f / Mathf.Max(1, nm));
				Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (anchorSize + 2f + (float)r.NextDouble() * Mathf.Max(1f, radius - anchorSize - 3f));
				put(p, a + 180f, 0.05f, One(r, medium));
			}
			int ns = small.Length == 0 ? 0 : 3 + r.Next(3);
			for (int i = 0; i < ns; i++)
			{
				Vector3 p = c + Quaternion.Euler(0f, Yaw(r), 0f) * Vector3.forward * (anchorSize + 1f + (float)r.NextDouble() * Mathf.Max(1f, radius - anchorSize - 1f));
				put(p, Yaw(r), 0.05f, One(r, small));
			}
			Vector3 box = c + Quaternion.Euler(0f, y0 + 180f, 0f) * Vector3.forward * (anchorSize + 1.5f), bh, bn;
			if (g.Hit(box.x, box.z, out bh, out bn)) k.Chest(t.Container, bh, Yaw(r), t.Label, MapKit.Loot(t.Loot));
			Vector3 note = c + Quaternion.Euler(0f, y0 + 150f, 0f) * Vector3.forward * (anchorSize + 2.5f), nh, nn;
			if (g.Hit(note.x, note.z, out nh, out nn)) k.Note(t.NoteKind, nh, Yaw(r), t.NoteTitle, One(r, t.Notes));
			return true;
		}

		#endregion

		#region Caves

		/// <summary>
		/// A cave: one of Balboa's dens (a rock outcrop with a den inside and one mouth), placed so its floor meets the
		/// ground at the mouth and the mouth faces open ground. Positions are in the kit's space (terrain-local for a
		/// generated island, world for Raft's islands).
		/// </summary>
		public class Cave
		{
			public string Name;
			public PropInfo Info;
			public Vector3 Pivot;
			public float Yaw;
			/// <summary>The middle of the mouth and the back wall, on the floor; Out = the way out (flat, normalised).</summary>
			public Vector3 Mouth, Back;
			public Vector2 Out;
			public float Floor, Width, Headroom;
			public float Depth { get { return Vector2.Distance(new Vector2(Mouth.x, Mouth.z), new Vector2(Back.x, Back.z)); } }
			/// <summary>A point on the floor t metres in from the mouth, s metres to the side.</summary>
			public Vector3 At(float t, float s = 0f) { return new Vector3(Mouth.x - Out.x * t - Out.y * s, Floor, Mouth.z - Out.y * t + Out.x * s); }
			/// <summary>How far from the middle the den's outcrop reaches (for finding room for it).</summary>
			public float Reach { get { return Mathf.Max(Info.Size.x, Info.Size.z) * 0.5f + 1f; } }
		}

		/// <summary>Raft's dens: whole caves with one mouth (Balboa's bear cave and dead end), as measured in raft_props.txt.</summary>
		public static readonly string[] Dens = { "BalboaCave_Bear", "BalboaCave_DeadEnd" };

		static PropInfo Den(string name)
		{
			PropInfo p = RaftProps.Get(name);
			return p != null && p.IsCave && p.OpenPlus != p.OpenMinus && p.Width > 3f && p.Headroom > 2.5f ? p : null;
		}

		/// <summary>Whether this Raft has measured dens to make caves with.</summary>
		public static bool CanBuildCaves { get { return Dens.Any(n => Den(n) != null); } }

		/// <summary>
		/// A den placed with its inside point (the measured spot in its passage) at inside, its mouth facing outward, its
		/// floor at floorY.
		/// </summary>
		public static Cave PlaceDen(string name, Vector2 inside, Vector2 outward, float floorY)
		{
			PropInfo p = Den(name);
			if (p == null) return null;
			outward = outward.normalized;
			Vector3 axis = p.Axis == 0 ? Vector3.right : Vector3.forward;
			Vector3 localOut = axis * (p.OpenPlus ? 1f : -1f);
			float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg - Mathf.Atan2(localOut.x, localOut.z) * Mathf.Rad2Deg;
			Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
			Vector3 o = rot * p.Inside;
			var cave = new Cave { Name = name, Info = p, Yaw = yaw, Out = outward, Floor = floorY, Width = p.Width, Headroom = p.Headroom };
			cave.Pivot = new Vector3(inside.x - o.x, floorY - p.Floor, inside.y - o.z);
			// The mouth: where the outcrop ends on the open side; the back: the closed side's wall
			float insideAlong = p.Axis == 0 ? p.Inside.x : p.Inside.z, centreAlong = p.Axis == 0 ? p.Centre.x : p.Centre.z, half = (p.Axis == 0 ? p.Size.x : p.Size.z) / 2f;
			float toMouth = Mathf.Max(1f, p.OpenPlus ? centreAlong + half - insideAlong : insideAlong - (centreAlong - half));
			float toBack = Mathf.Max(2f, (p.OpenPlus ? p.ToMinus : p.ToPlus) - 0.4f);
			cave.Mouth = new Vector3(inside.x + outward.x * toMouth, floorY, inside.y + outward.y * toMouth);
			cave.Back = new Vector3(inside.x - outward.x * toBack, floorY, inside.y - outward.y * toBack);
			return cave;
		}

		/// <summary>What is inside a cave: a hoard at the back, a lantern by it, dark air, and a guard that wakes when players come in.</summary>
		static void FurnishCave(object kit, Cave cave, string guard, string lootPreset, string title)
		{
			MapKit mk = kit as MapKit;
			WorldKit wk = kit as WorldKit;
			float depth = cave.Depth, inward = Mathf.Atan2(-cave.Out.x, -cave.Out.y) * Mathf.Rad2Deg;
			Vector3 hoard = cave.At(depth - 2f), lamp = cave.At(depth - 2.5f, cave.Width * 0.3f), guardAt = cave.At(depth * 0.7f), zoneAt = cave.At(Mathf.Min(6f, depth * 0.3f));
			var chest = new Dictionary<string, string> { { ObjectProps.LootItems, MapKit.Loot(lootPreset) + ";" + MapKit.Loot("Metal").Split(';').First() }, { ObjectProps.NoteTitle, title } };
			var zone = new Dictionary<string, string> { { ObjectProps.ZoneId, "cave" }, { ObjectProps.ZoneRadius, Mathf.Clamp(cave.Width * 0.45f, 2.5f, 6f).ToString("0.#", CultureInfo.InvariantCulture) }, { ObjectProps.ZoneMessage, "Something moves in the dark..." } };
			var atmo = new Dictionary<string, string>
			{
				{ ObjectProps.ZoneRadius, Mathf.Max(6f, depth * 0.5f).ToString("0.#", CultureInfo.InvariantCulture) }, { ObjectProps.AtmoFog, "#0E0C0A" }, { ObjectProps.AtmoFogAmount, "0.5" },
				{ ObjectProps.AtmoLight, "#FFB060" }, { ObjectProps.AtmoLightAmount, "0.2" }, { ObjectProps.AtmoParticles, "none" },
			};
			Dictionary<string, string> creature = null;
			if (guard != null)
			{
				creature = new Dictionary<string, string> { { ObjectProps.CreatureCount, guard == "Rat" ? "3" : "1" }, { ObjectProps.CreatureZone, "cave" }, { ObjectProps.CreatureRespawn, "0" } };
				if (guard != "Rat") { creature[ObjectProps.CreatureHealth] = "2"; creature[ObjectProps.CreatureDamage] = "1.5"; creature[ObjectProps.CreatureSize] = "1.1"; }
			}
			Action<string, Vector3, float, Dictionary<string, string>> add = (n, at, yaw, props) =>
			{
				if (mk != null) mk.Add(n, at, yaw, props, 0f); else wk.Add(n, at, yaw, props);
			};
			add(cave.Name, cave.Pivot, cave.Yaw, new Dictionary<string, string> { { "set.piece", "1" }, { "cave", "1" } });
			add("Loot_Chest", hoard, inward + 180f, chest);
			add("Placeable_Lantern_Basic", lamp, 0f, null);
			add(ContentCatalog.TriggerZone, zoneAt, 0f, zone);
			add(ContentCatalog.AtmosphereZoneName, cave.At(depth * 0.5f), 0f, atmo);
			if (creature != null) add("Creature_" + guard, guardAt, inward + 180f, creature);
		}

		static string DenFor(System.Random r) { string[] dens = Dens.Where(n => Den(n) != null).ToArray(); return dens.Length > 0 ? dens[r.Next(dens.Length)] : null; }

		/// <summary>
		/// A den on a generated island: on fairly level land near the coast, its mouth towards the sea. The ground under
		/// it is levelled to the den's floor and blended into the land around, nature is cleared from it and in front of
		/// the mouth. Returns false if no spot fits.
		/// </summary>
		public static bool EmbeddedCave(MapKit k, IslandGenSettings s, string guard, string lootPreset, string title)
		{
			System.Random r = k.Rnd;
			string name = DenFor(r);
			if (name == null) return false;
			PropInfo p = Den(name);
			float reach = Mathf.Max(p.Size.x, p.Size.z) * 0.5f;
			for (int attempt = 0; attempt < 300; attempt++)
			{
				float a = (float)r.NextDouble() * Mathf.PI * 2f, d = (0.25f + 0.55f * (float)r.NextDouble()) * s.Radius;
				Vector2 c = k.Mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
				float floor = k.Ground(c);
				if (floor - k.Sea < 2f || floor - k.Sea > 25f) continue;
				Vector2 outward = (c - k.Mid).normalized;
				// All of the outcrop over land, not much lower than the floor (it reaches 6-11 m under it); open ground in front
				bool ok = true;
				for (int i = 0; i < 16 && ok; i++)
				{
					float b = i * Mathf.PI / 8f;
					Vector2 q = c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * reach;
					float g = k.Ground(q);
					if (g - k.Sea < 0.5f || g < floor - 4f) ok = false;
				}
				for (float t = reach; t <= reach + 10f && ok; t += 2f) { float g = k.Ground(c + outward * t); if (g - k.Sea < 0.4f || Mathf.Abs(g - floor) > 3f) ok = false; }
				if (!ok) continue;
				Cave cave = PlaceDen(name, c, outward, floor);
				Level(k, new Vector2(cave.Pivot.x, cave.Pivot.z) + Vector2.zero, c, reach, floor, outward);
				k.Clear(c, reach + 3f);
				for (float t = reach; t <= reach + 10f; t += 2f) k.Clear(c + outward * t, 4f);
				FurnishCave(k, cave, guard, lootPreset, title);
				return true;
			}
			return false;
		}

		/// <summary>The ground under a den levelled to its floor out to its reach, blended into the land beyond; a level path out of the mouth.</summary>
		static void Level(MapKit k, Vector2 pivot, Vector2 c, float reach, float floor, Vector2 outward)
		{
			IslandFile f = k.File;
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1), blend = 10f;
			int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - reach - blend - 12f) / step)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.x + reach + blend + 12f) / step));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((c.y - reach - blend - 12f) / step)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.y + reach + blend + 12f) / step));
			Vector2 side = new Vector2(-outward.y, outward.x);
			for (int z = z0; z <= z1; z++)
				for (int x = x0; x <= x1; x++)
				{
					Vector2 q = new Vector2(x * step, z * step);
					float h = f.Heights[z, x] * f.TerrainSize.y, d = (q - c).magnitude;
					// (a path out of the mouth: level for 12 m past the outcrop, 8 m wide)
					float along = Vector2.Dot(q - c, outward), across = Mathf.Abs(Vector2.Dot(q - c, side));
					bool path = along > 0f && along < reach + 12f && across < 4f;
					float w = d <= reach || path ? 1f : d < reach + blend ? 1f - (d - reach) / blend : 0f;
					if (w <= 0f) continue;
					w = w * w * (3f - 2f * w);
					f.Heights[z, x] = Mathf.Clamp01(Mathf.Lerp(h, floor - 0.15f, w) / f.TerrainSize.y);
				}
		}

		/// <summary>
		/// A den on one of Raft's islands, whose ground can't be changed: a spot where the den's inside is clear of Raft's
		/// trees and rocks and its floor meets the ground, with walkable ground out of the mouth. Returns a short
		/// description, or null if nothing fits.
		/// </summary>
		public static string Grotto(WorldKit k, LandGround g, System.Random r, int style, string guard, string lootPreset)
		{
			string name = DenFor(r);
			if (name == null) return null;
			PropInfo p = Den(name);
			float reach = Mathf.Max(p.Size.x, p.Size.z) * 0.5f;
			for (int attempt = 0; attempt < 80; attempt++)
			{
				Vector3? spot = g.Find(r, (h, slope) => h > 2f && h < 30f && slope < 12f, null, 0f, 3);
				if (!spot.HasValue) continue;
				// Out towards the coast (away from the island's middle), turned a little
				Vector2 c = new Vector2(spot.Value.x, spot.Value.z), outward = (c - new Vector2(g.Centre.x, g.Centre.z)).normalized;
				if (outward.sqrMagnitude < 0.5f) continue;
				outward = (Vector2)(Quaternion.Euler(0f, 0f, (float)r.NextDouble() * 50f - 25f) * outward);
				Cave cave = PlaceDen(name, c, outward, spot.Value.y);
				// Inside the den: the ground at or under its floor (Raft's rocks or slopes there would poke in), nothing of Raft's in it
				bool ok = true;
				for (float t = 1f; t <= cave.Depth && ok; t += 2f)
					foreach (float s in new[] { -cave.Width * 0.35f, 0f, cave.Width * 0.35f })
					{
						Vector3 q = cave.At(t, s), hit, n;
						if (!g.Hit(q.x, q.z, out hit, out n) || hit.y > cave.Floor + 1.2f || hit.y < cave.Floor - 3f) { ok = false; break; }
						if (k.Taken.Any(x => (new Vector2(x.x - q.x, x.z - q.z)).sqrMagnitude < 9f)) { ok = false; break; }
					}
				// Around it: land under the whole outcrop (not hanging over the sea); out of the mouth: walkable
				for (int i = 0; i < 12 && ok; i++)
				{
					float b = i * Mathf.PI / 6f;
					Vector3 q = new Vector3(c.x + Mathf.Cos(b) * reach, 0f, c.y + Mathf.Sin(b) * reach), hit, n;
					if (!g.Hit(q.x, q.z, out hit, out n) || hit.y < 0.5f || hit.y < cave.Floor - 5f) ok = false;
				}
				for (float t = 1f; t <= 8f && ok; t += 2f)
				{
					Vector3 q = cave.At(-t), hit, n;
					if (!g.Hit(q.x, q.z, out hit, out n) || Mathf.Abs(hit.y - cave.Floor) > 1.5f || Vector3.Angle(n, Vector3.up) > 30f) ok = false;
				}
				if (!ok) continue;
				FurnishCave(k, cave, guard, lootPreset, "Den hoard");
				return "a den (" + name + ", " + cave.Depth.ToString("F0", CultureInfo.InvariantCulture) + " m deep" + (guard != null ? ", a " + guard.ToLowerInvariant() : "") + ")";
			}
			return null;
		}

		#endregion

		#region Large islands

		static readonly string[] NameFirst = { "Gull", "Palm", "Driftwood", "Coral", "Turtle", "Crab", "Storm", "Lantern", "Old Man's", "Whisper", "Sunset", "Thunder", "Mango", "Anchor", "Fisher's", "Kelp" };
		static readonly string[] NameLast = { "Isle", "Rock", "Key", "Haven", "Reach", "Point", "Island", "Hollow", "Bluff", "Cay" };

		/// <summary>A name for a large island, from its seed.</summary>
		public static string IslandName(System.Random r) { return One(r, NameFirst) + " " + One(r, NameLast); }

		/// <summary>Settings of a large island: as big and tall as Raft's big islands, objects as dense as theirs.</summary>
		public static IslandGenSettings LargeSettings(System.Random rnd)
		{
			double roll = rnd.NextDouble();
			int style = roll < 0.55 ? Tropical : roll < 0.7 ? Forest : roll < 0.8 ? Desert : roll < 0.9 ? Snowy : Volcanic;
			int[] shapes = { IslandShapes.Round, IslandShapes.Round, IslandShapes.Crescent, IslandShapes.TwinPeaks };
			var s = new IslandGenSettings
			{
				Seed = rnd.Next(1, 999999), Style = style, Shape = shapes[rnd.Next(shapes.Length)],
				Radius = 78f + (float)rnd.NextDouble() * 30f, Height = 36f + (float)rnd.NextDouble() * 22f,
				Roughness = 0.45f + (float)rnd.NextDouble() * 0.2f, Peaks = 2 + rnd.Next(3), ObjectDensity = 0.6f,
			};
			LikeRaft(s);
			// Like Raft's big islands: warthogs, animals to catch for the raft, puffer fish around (content), loot boxes
			s.Hostiles = 3;
			s.HostileKinds = style == Tropical ? "Boar" : "";
			s.Friendly = 4;
			s.Loot = 4; s.LootMin = 2; s.LootMax = 4; s.LootHidden = true;
			return s;
		}

		/// <summary>Every land object slider "like Raft" (the generator's measured densities at Raft's own amount).</summary>
		public static void LikeRaft(IslandGenSettings s)
		{
			s.Trees = s.Bushes = 0.33f;
			s.Rocks = s.Harvest = 0.33f;
			s.BeachThings = 0.3f;
		}

		/// <summary>
		/// A large island's content, like Raft's big islands and more: puffer fish around it, a screecher, one or two themed
		/// scenes from the quest islands, and a cave with a guard and a hoard.
		/// </summary>
		public static void Large(MapKit k, IslandGenSettings s)
		{
			System.Random r = k.Rnd;
			k.File.Props[IslandProps.Title] = IslandName(r);
			// Puffer fish around, under water (Raft's big islands have about six)
			for (int i = 0; i < 5; i++)
			{
				Vector2? p = k.Find(k.Mid, s.Radius * 1.4f, (above, slope) => above < -4f && above > -14f && slope < 40f, 12f);
				if (p.HasValue) k.Creature("PufferFish", p.Value, 1, "Normal", 1f, null, true, 2f);
			}
			if (s.Style == Tropical)
			{
				Vector2 high = k.Highest(k.Mid, s.Radius * 0.6f);
				k.Creature("StoneBird", high, 1, "Normal", 1f, null, true, 2f);
			}
			// Themed scenes on open, flat ground
			List<Theme> themes = ThemesFor(s.Style);
			int scenes = themes.Count == 0 ? 0 : 1 + r.Next(2);
			var used = new List<Vector2>();
			for (int i = 0; i < scenes; i++)
			{
				Theme t = themes[r.Next(themes.Count)];
				themes.Remove(t);
				Vector2? spot = k.Find(k.Mid, s.Radius * 0.75f, (above, slope) => above > 2.5f && above < 25f && slope < 10f, 30f);
				if (!spot.HasValue || used.Any(u => (u - spot.Value).magnitude < 35f)) continue;
				if (Dress(k, t, spot.Value, 9f)) used.Add(spot.Value);
				if (themes.Count == 0) break;
			}
			// A cave with a guard and a hoard
			string guard = s.Style == Snowy ? "PolarBear" : s.Style == Forest ? "Bear" : s.Style == Desert ? "Hyena" : r.NextDouble() < 0.5 ? "Boar" : "Rat";
			EmbeddedCave(k, s, guard, "Treasure", "Cave hoard");
		}

		#endregion
	}
}
