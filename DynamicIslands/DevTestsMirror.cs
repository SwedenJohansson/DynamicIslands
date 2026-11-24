using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIMirrorIslands", docs: "Dev, anywhere: Mirrored islands (PF8) without a world. A made-up island is mirrored: its heights, paint, paint mask, objects and their rotations flip left to right, the land centre mirrors and the radius stays, uids and order stay, twice gives the original. Quests, behaviours, land.centre, other tails, rules and labels keep an island unmirrored; the choice is fixed per seed and name, about one in three, and only with the option on.")]
		public static void MirrorIslandsTestCommand(string[] args) { StartTest(MirrorIslandsRoutine()); }

		static IslandFile MirrorSample()
		{
			const int res = 33, r = 16, layers = 2;
			var f = new IslandFile { Name = "CIMirrorSample", TerrainSize = new Vector3(200f, 100f, 200f), HeightmapResolution = res, Heights = new float[res, res] };
			float water = f.WaterLevel / f.TerrainSize.y;
			// A lopsided hill: land west of the middle, higher towards the north-west
			for (int y = 0; y < res; y++)
				for (int x = 0; x < res; x++)
				{
					float d = Mathf.Sqrt((x - 10f) * (x - 10f) + (y - 18f) * (y - 18f));
					f.Heights[y, x] = d < 8f ? water + (8f - d) * 0.01f + x * 0.0001f + y * 0.0002f : water * 0.5f;
				}
			f.AlphamapResolution = r; f.AlphamapLayers = layers; f.Alphamaps = new byte[layers * r * r]; f.PaintMask = new byte[r * r];
			for (int i = 0; i < f.Alphamaps.Length; i++) f.Alphamaps[i] = (byte)((i * 37) % 256);
			for (int i = 0; i < f.PaintMask.Length; i++) f.PaintMask[i] = (byte)(i % 3 == 0 ? 255 : 0);
			f.Objects.Add(new IslandObject { Name = "Palm", Position = new Vector3(60f, 41f, 90f), EulerRotation = new Vector3(0f, 30f, 0f), Uid = 1 });
			f.Objects.Add(new IslandObject { Name = "Crate", Position = new Vector3(75f, 42f, 100f), EulerRotation = new Vector3(10f, 200f, 5f), Uid = 7,
				Props = new Dictionary<string, string> { { "loot.items", "Plank*3" }, { "tint.color", "1,0,0" } } });
			f.Objects.Add(new IslandObject { Name = "Note", Position = new Vector3(55f, 40.5f, 80f), EulerRotation = new Vector3(0f, 90f, 0f), Uid = 3,
				Props = new Dictionary<string, string> { { "note.text", "hello" } } });
			f.Props["info.title"] = "Mirror sample";
			return f;
		}

		static IEnumerator MirrorIslandsRoutine()
		{
			bool ok = true;
			IslandFile orig = MirrorSample(), m = MirrorSample();
			string why;
			Check(ref ok, MirrorIslands.CanMirror(m, out why), "a plain island can be mirrored" + (why != "" ? " - not: " + why : ""));
			Vector2 c0 = IslandSpawner.LandCentre(orig);
			float r0 = IslandSpawner.LandRadius(orig);
			MirrorIslands.Mirror(m);
			int res = orig.HeightmapResolution, R = orig.AlphamapResolution;
			bool heights = true;
			for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) if (m.Heights[y, x] != orig.Heights[y, res - 1 - x]) heights = false;
			Check(ref ok, heights, "heights flipped left to right");
			bool paint = true;
			for (int l = 0; l < orig.AlphamapLayers; l++) for (int z = 0; z < R; z++) for (int x = 0; x < R; x++)
				if (m.Alphamaps[(l * R + z) * R + x] != orig.Alphamaps[(l * R + z) * R + R - 1 - x]) paint = false;
			for (int z = 0; z < R; z++) for (int x = 0; x < R; x++) if (m.PaintMask[z * R + x] != orig.PaintMask[z * R + R - 1 - x]) paint = false;
			Check(ref ok, paint, "paint and paint mask flipped");
			Vector2 c1 = IslandSpawner.LandCentre(m);
			float r1 = IslandSpawner.LandRadius(m);
			Check(ref ok, Mathf.Abs(c1.x - (orig.TerrainSize.x - c0.x)) < 0.01f && Mathf.Abs(c1.y - c0.y) < 0.01f && Mathf.Abs(r1 - r0) < 0.01f,
				"land centre " + c0 + " -> " + c1 + " (mirrored), radius " + r0 + " -> " + r1);
			bool objs = m.Objects.Count == orig.Objects.Count;
			for (int i = 0; objs && i < orig.Objects.Count; i++)
			{
				IslandObject a = orig.Objects[i], b = m.Objects[i];
				Vector3 fa = Quaternion.Euler(a.EulerRotation) * Vector3.forward, fb = Quaternion.Euler(b.EulerRotation) * Vector3.forward;
				Vector3 ua = Quaternion.Euler(a.EulerRotation) * Vector3.up, ub = Quaternion.Euler(b.EulerRotation) * Vector3.up;
				objs = a.Name == b.Name && a.Uid == b.Uid && Mathf.Abs(b.Position.x - (orig.TerrainSize.x - a.Position.x)) < 0.001f && b.Position.y == a.Position.y && b.Position.z == a.Position.z
					&& (fb - new Vector3(-fa.x, fa.y, fa.z)).magnitude < 0.001f && (ub - new Vector3(-ua.x, ua.y, ua.z)).magnitude < 0.001f
					&& (a.Props == null ? b.Props == null : b.Props != null && a.Props.Count == b.Props.Count && a.Props.All(kv => b.Props.ContainsKey(kv.Key) && b.Props[kv.Key] == kv.Value));
				if (!objs) Log("object " + i + " " + a.Name + ": " + a.Position + " " + a.EulerRotation + " -> " + b.Position + " " + b.EulerRotation);
			}
			Check(ref ok, objs, "objects: x mirrored, facing and up mirrored, same order, names, uids and settings");
			MirrorIslands.Mirror(m);
			bool back = true;
			for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) if (m.Heights[y, x] != orig.Heights[y, x]) back = false;
			if (!m.Alphamaps.SequenceEqual(orig.Alphamaps) || !m.PaintMask.SequenceEqual(orig.PaintMask)) back = false;
			for (int i = 0; i < orig.Objects.Count; i++)
				if ((m.Objects[i].Position - orig.Objects[i].Position).magnitude > 0.001f || Quaternion.Angle(Quaternion.Euler(m.Objects[i].EulerRotation), Quaternion.Euler(orig.Objects[i].EulerRotation)) > 0.05f) back = false;
			Check(ref ok, back, "mirrored twice = the original");

			// What keeps an island as it is
			var blocked = new List<KeyValuePair<string, Action<IslandFile>>>
			{
				new KeyValuePair<string, Action<IslandFile>>("a quest", f => f.Props[IslandQuest.KeySteps] = "Find the crate"),
				new KeyValuePair<string, Action<IslandFile>>("a land.centre (Raft island extras)", f => f.Props[IslandProps.Centre] = "100,100"),
				new KeyValuePair<string, Action<IslandFile>>("an island event", f => f.Props["on.arrive"] = "message:hi"),
				new KeyValuePair<string, Action<IslandFile>>("a behaviour", f => f.Objects[0].Props = new Dictionary<string, string> { { "beh.spin", "10" } }),
				new KeyValuePair<string, Action<IslandFile>>("a zip line", f => f.Objects[0].Props = new Dictionary<string, string> { { "zip.to", "2" } }),
				new KeyValuePair<string, Action<IslandFile>>("a push zone", f => f.Objects[0].Props = new Dictionary<string, string> { { "zone.push", "1,0,0" } }),
				new KeyValuePair<string, Action<IslandFile>>("a story item", f => f.Objects[0].Props = new Dictionary<string, string> { { "story.item", "x" } }),
				new KeyValuePair<string, Action<IslandFile>>("a tail block it doesn't know", f => f.Tail["future"] = new byte[1]),
				new KeyValuePair<string, Action<IslandFile>>("no land", f => { for (int y = 0; y < f.HeightmapResolution; y++) for (int x = 0; x < f.HeightmapResolution; x++) f.Heights[y, x] = 0f; }),
			};
			foreach (var b in blocked)
			{
				IslandFile f = MirrorSample();
				b.Value(f);
				Check(ref ok, !MirrorIslands.CanMirror(f, out why), "not mirrored with " + b.Key + " (" + why + ")");
			}
			IslandFile withUids = MirrorSample();
			withUids.Tail[StableIds.Tag] = new byte[4];
			Check(ref ok, MirrorIslands.CanMirror(withUids, out why), "the uid tail alone is fine" + (why != "" ? " - not: " + why : ""));

			// The choice: fixed, about one in three, only with the option, never for a ruled or labelled entry
			int picked = 0;
			for (int i = 0; i < 600; i++) if (MirrorIslands.Picked(42, "Island" + i)) picked++;
			Check(ref ok, picked > 140 && picked < 260 && MirrorIslands.Picked(42, "Coral") == MirrorIslands.Picked(42, "coral") && MirrorIslands.Picked(7, "Coral") == MirrorIslands.Picked(7, "Coral"),
				picked + " of 600 names picked (about 200), the same each time");
			bool hadOption = WorldOptions.Current.Contains(WorldOptions.MirrorIslands);
			int seed = WorldOptions.Seed;
			try
			{
				int s = 1;
				while (!MirrorIslands.Picked(s, "CIMirrorSample")) s++;
				WorldOptions.Seed = s;
				var plain = new IslandWorldState.Entry { Name = "CIMirrorSample", HostName = "CIMirrorSample" };
				WorldOptions.Current.Remove(WorldOptions.MirrorIslands);
				Check(ref ok, !MirrorIslands.ShouldMirror(plain, MirrorSample()), "option off: never mirrored");
				WorldOptions.Current.Add(WorldOptions.MirrorIslands);
				Check(ref ok, MirrorIslands.ShouldMirror(plain, MirrorSample()), "option on, picked (seed " + s + "): mirrored");
				Check(ref ok, !MirrorIslands.ShouldMirror(new IslandWorldState.Entry { Name = "CIMirrorSample", HostName = "CIMirrorSample", Rule = "plan" }, MirrorSample())
					&& !MirrorIslands.ShouldMirror(new IslandWorldState.Entry { Name = "CIMirrorSample", HostName = "CIMirrorSample", Label = "story" }, MirrorSample())
					&& !MirrorIslands.ShouldMirror(null, MirrorSample()), "a ruled or labelled entry, or no entry (editor Test, SpawnIsland): never");
				int t = s + 1;
				while (MirrorIslands.Picked(t, "CIMirrorSample")) t++;
				WorldOptions.Seed = t;
				Check(ref ok, !MirrorIslands.ShouldMirror(plain, MirrorSample()), "not picked (seed " + t + "): not mirrored");
			}
			finally
			{
				WorldOptions.Seed = seed;
				if (hadOption) WorldOptions.Current.Add(WorldOptions.MirrorIslands); else WorldOptions.Current.Remove(WorldOptions.MirrorIslands);
			}
			yield return null;
			if (ok) Log("PASS: mirror islands"); else Fail("mirror islands");
		}
	}
}
