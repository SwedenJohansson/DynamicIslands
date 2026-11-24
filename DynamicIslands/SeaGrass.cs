using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's short sea grass on the sea floor (ROADMAP PF-D3). Raft grows it as a terrain detail layer (texture
	/// "Sea_grass", 0.5-1 m tall, green to brown), not as objects, so the island's terrain gets one detail layer too: one
	/// cell per heightmap cell (about 2 m), 0 = bare, 1-2 = tufts. The editor paints it with the Sea grass brush (only under
	/// water; Shift takes it away), the island file keeps it in the tail block "grass", and a spawned island shows the
	/// cells of its crop that lie under the sea (never on a flying island).
	/// The look is learned from Raft's own terrains while the catalog visits Raft's scenes (Learn); until then (or if a
	/// Raft update renames it) a drawn tuft stands in.
	/// A grass terrain starts as a copy of a Raft terrain (NewTerrainData): on a TerrainData made by the mod, Unity drew
	/// every tuft as a whole card, even with Raft's very layer; on a copy of Raft's the blades are cut out (probed 10-26).
	/// </summary>
	public static class SeaGrass
	{
		public const string Tag = "grass";
		/// <summary>Cells per side on the editor's terrain: one per heightmap cell (heightmap resolution 513).</summary>
		public const int Resolution = 512;
		public const int MaxDensity = 2;
		/// <summary>Grass grows only where the ground is this far under the sea.</summary>
		public const float MinDepth = 0.4f;
		const string RaftTexture = "Sea_grass";
		const float DrawDistance = 120f;

		static DetailPrototype learned;
		/// <summary>A small copy of a Raft terrain with only the short sea grass layer: the start of the grass terrains.</summary>
		static TerrainData template;
		const string BornName = "CustomIslands_GrassTerrain";
		static Texture2D drawn;
		// (Raft's own terrains' waving, measured 10-26; its tint's alpha is 0. With Unity's defaults and a white tint every
		// tuft stood as a whole opaque card)
		static float waveStrength = 0.5f, waveAmount = 0.191f, waveSpeed = 0.5f;
		static Color waveTint = new Color(1f, 1f, 1f, 0f);

		/// <summary>True once Raft's own sea grass has been found.</summary>
		public static bool FromRaft { get { return learned != null && learned.prototypeTexture != null; } }

		/// <summary>From a Raft terrain the catalog visits: its short sea grass layer, kept for the islands' terrains.</summary>
		public static void Learn(TerrainData d)
		{
			if ((FromRaft && template != null) || d == null) return;
			try
			{
				foreach (DetailPrototype p in d.detailPrototypes)
				{
					if (p == null || p.usePrototypeMesh || p.prototypeTexture == null || p.prototypeTexture.name != RaftTexture || p.maxHeight > 1.01f) continue;
					learned = Copy(p, Readable(p.prototypeTexture));
					MakeTemplate(d);
					waveStrength = d.wavingGrassStrength; waveAmount = d.wavingGrassAmount; waveSpeed = d.wavingGrassSpeed; waveTint = d.wavingGrassTint;
					Debug.Log("[CUSTOM ISLANDS] Sea grass: Raft's short sea grass found (" + p.minHeight + "-" + p.maxHeight + " m)");
					if (terraineditor.terrain != null) Refresh(terraineditor.terrain);
					return;
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Sea grass: " + e.Message); }
		}

		static void MakeTemplate(TerrainData raft)
		{
			if (template != null) return;
			try
			{
				TerrainData d = UnityEngine.Object.Instantiate(raft);
				d.name = BornName + "Template";
				d.hideFlags = HideFlags.DontUnloadUnusedAsset;
				d.treeInstances = new TreeInstance[0];
				d.treePrototypes = new TreePrototype[0];
				d.heightmapResolution = 33;
				d.size = new Vector3(32f, 32f, 32f);
				d.SetHeights(0, 0, new float[33, 33]);
				var solid = new bool[32, 32];
				for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) solid[z, x] = true;
				d.SetHoles(0, 0, solid);
				d.terrainLayers = new TerrainLayer[0];
				d.detailPrototypes = new[] { Copy(learned, learned.prototypeTexture) };
				d.SetDetailResolution(32, 16);
				template = d;
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Sea grass: no terrain copy: " + e.Message); }
		}

		/// <summary>
		/// A new terrain data that draws the sea grass right: a copy of the template (a plain TerrainData until a Raft terrain
		/// with the grass has been seen). Set its heightmap resolution and size as with a new one.
		/// </summary>
		public static TerrainData NewTerrainData()
		{
			if (template == null) Prototype();
			if (template == null) return new TerrainData();
			TerrainData d = UnityEngine.Object.Instantiate(template);
			d.name = BornName;
			d.hideFlags = HideFlags.None;
			return d;
		}

		/// <summary>True when the terrain data began as a copy of Raft's (its grass is cut out).</summary>
		public static bool CutsOut(TerrainData d) { return d != null && d.name == BornName; }

		/// <summary>
		/// The editor's terrain moved onto a copy of Raft's terrain data (when Raft's grass was found after the editor
		/// made its terrain): heights, paint, holes and grass carried over. Undo history is cleared (it points at the old data).
		/// </summary>
		static void Adopt(Terrain t)
		{
			TerrainData old = t.terrainData;
			if (old == null || CutsOut(old) || template == null) return;
			TerrainData d = NewTerrainData();
			int hres = old.heightmapResolution, ares = old.alphamapResolution;
			d.heightmapResolution = hres;
			d.size = old.size;
			d.SetHeights(0, 0, old.GetHeights(0, 0, hres, hres));
			d.SetHoles(0, 0, old.GetHoles(0, 0, hres - 1, hres - 1));
			d.terrainLayers = old.terrainLayers;
			d.alphamapResolution = ares;
			if (old.alphamapLayers > 0) d.SetAlphamaps(0, 0, old.GetAlphamaps(0, 0, ares, ares));
			int[,] grass = old.detailPrototypes.Length == 1 ? old.GetDetailLayer(0, 0, old.detailWidth, old.detailHeight, 0) : null;
			d.SetDetailResolution(grass != null ? old.detailWidth : Resolution, 16);
			if (grass != null) d.SetDetailLayer(0, 0, 0, grass);
			t.terrainData = d;
			TerrainCollider c = t.GetComponent<TerrainCollider>();
			if (c != null) c.terrainData = d;
			if (terraineditor.terrain == t) terraineditor.terrainData = d;
			CommandUndoRedo.UndoRedoManager.Clear();
			UnityEngine.Object.Destroy(old);
			Debug.Log("[CUSTOM ISLANDS] Sea grass: the editor's terrain now draws Raft's sea grass");
		}

		static DetailPrototype Copy(DetailPrototype p, Texture2D texture)
		{
			return new DetailPrototype
			{
				usePrototypeMesh = false, prototypeTexture = texture, renderMode = DetailRenderMode.Grass,
				minWidth = p.minWidth, maxWidth = p.maxWidth, minHeight = p.minHeight, maxHeight = p.maxHeight,
				healthyColor = p.healthyColor, dryColor = p.dryColor, noiseSpread = p.noiseSpread,
			};
		}

		/// <summary>The grass layer's look: Raft's, or a drawn tuft in Raft's sizes and colours.</summary>
		public static DetailPrototype Prototype()
		{
			if (!FromRaft || template == null)
			{
				// (a Raft scene with it may be loaded already: the editor's catalog cache skips the scene visits)
				TerrainData raft = Resources.FindObjectsOfTypeAll<TerrainData>().FirstOrDefault(x => x != null && x.detailPrototypes.Any(p => !p.usePrototypeMesh && p.prototypeTexture != null && p.prototypeTexture.name == RaftTexture));
				if (raft != null) Learn(raft);
			}
			if (!FromRaft)
			{
				Texture2D found = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t != null && t.name == RaftTexture);
				if (found != null)
				{
					learned = Copy(Fallback(), Readable(found));
				}
			}
			if (FromRaft) return Copy(learned, learned.prototypeTexture);
			return Fallback();
		}

		/// <summary>
		/// A readable copy of Raft's grass texture. A new terrain packs its grass textures into an atlas, which needs
		/// readable textures; Raft's own terrains carry theirs ready made. With the unreadable original a new terrain drew
		/// every tuft as an opaque card (seen 10-26). The copy keeps the name and the alpha.
		/// </summary>
		static Texture2D Readable(Texture2D source)
		{
			if (source.isReadable) { source.hideFlags |= HideFlags.DontUnloadUnusedAsset; return source; }
			RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			RenderTexture was = RenderTexture.active;
			try
			{
				Graphics.Blit(source, rt);
				RenderTexture.active = rt;
				var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true) { name = source.name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
				copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
				copy.Apply(true);
				return copy;
			}
			finally { RenderTexture.active = was; RenderTexture.ReleaseTemporary(rt); }
		}

		/// <summary>The share of a texture's pixels that are see-through (alpha under a half); -1 when it cannot be read.</summary>
		public static float ClearShare(Texture2D t)
		{
			if (t == null) return -1f;
			try
			{
				Texture2D r = t.isReadable ? t : Readable(t);
				Color32[] px = r.GetPixels32();
				if (r != t) UnityEngine.Object.Destroy(r);
				return px.Length == 0 ? -1f : px.Count(c => c.a < 128) / (float)px.Length;
			}
			catch { return -1f; }
		}

		static DetailPrototype Fallback()
		{
			if (drawn == null) drawn = DrawTuft();
			return new DetailPrototype
			{
				usePrototypeMesh = false, prototypeTexture = drawn, renderMode = DetailRenderMode.Grass,
				minWidth = 1f, maxWidth = 1.5f, minHeight = 0.5f, maxHeight = 1f, noiseSpread = 0.1f,
				healthyColor = new Color32(0x21, 0x6A, 0x47, 255), dryColor = new Color32(0x90, 0x5D, 0x42, 255),
			};
		}

		/// <summary>A few thin blades on a clear background (white: the layer's colours tint it).</summary>
		static Texture2D DrawTuft()
		{
			const int w = 32, h = 64;
			var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "CustomIslands_SeaGrass", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontUnloadUnusedAsset };
			var px = new Color32[w * h];
			var rnd = new System.Random(7);
			for (int b = 0; b < 7; b++)
			{
				float x0 = 4 + rnd.Next(w - 8), lean = (float)(rnd.NextDouble() - 0.5) * 10f, top = h * (0.55f + 0.45f * (float)rnd.NextDouble());
				for (int y = 0; y < top; y++)
				{
					float f = y / top, cx = x0 + lean * f * f, half = 1.6f * (1f - f) + 0.4f;
					byte shade = (byte)(170 + 85 * f);
					for (int x = Mathf.Max(0, (int)(cx - half)); x <= Mathf.Min(w - 1, (int)(cx + half)); x++) px[y * w + x] = new Color32(shade, shade, shade, 255);
				}
			}
			t.SetPixels32(px);
			t.Apply(true);
			return t;
		}

		/// <summary>Gives a terrain the grass layer at a resolution (clears what it had).</summary>
		public static void Setup(Terrain t, int resolution)
		{
			TerrainData d = t.terrainData;
			d.detailPrototypes = new[] { Prototype() };
			d.SetDetailResolution(resolution, 16);
			d.wavingGrassStrength = waveStrength; d.wavingGrassAmount = waveAmount; d.wavingGrassSpeed = waveSpeed; d.wavingGrassTint = waveTint;
			t.detailObjectDistance = DrawDistance;
			t.detailObjectDensity = 1f;
			t.drawTreesAndFoliage = true;
		}

		/// <summary>The editor's terrain: the grass layer, set up once (and its look swapped for Raft's once found).</summary>
		public static void EditorSetup(Terrain t)
		{
			if (t == null) return;
			if (!CutsOut(t.terrainData) && template != null) Adopt(t);
			TerrainData d = t.terrainData;
			if (d.detailPrototypes.Length != 1 || d.detailResolution != Resolution) Setup(t, Resolution);
			else Refresh(t);
		}

		/// <summary>The look swapped for Raft's grass when it was found later, keeping what is painted.</summary>
		static void Refresh(Terrain t)
		{
			if (!CutsOut(t.terrainData) && template != null) { Adopt(t); if (t.terrainData.detailPrototypes.Length != 1) return; }
			TerrainData d = t.terrainData;
			if (d.detailPrototypes.Length != 1) return;
			DetailPrototype want = Prototype();
			d.wavingGrassStrength = waveStrength; d.wavingGrassAmount = waveAmount; d.wavingGrassSpeed = waveSpeed; d.wavingGrassTint = waveTint;
			if (d.detailPrototypes[0].prototypeTexture == want.prototypeTexture) return;
			int[,] keep = d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, 0);
			d.detailPrototypes = new[] { want };
			d.SetDetailLayer(0, 0, 0, keep);
		}

		public static int[,] Layer(Terrain t)
		{
			TerrainData d = t.terrainData;
			return d.detailPrototypes.Length == 0 ? new int[0, 0] : d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, 0);
		}

		/// <summary>How many cells have grass.</summary>
		public static int Count(int[,] layer)
		{
			int n = 0;
			foreach (int v in layer) if (v > 0) n++;
			return n;
		}

		/// <summary>Takes all the grass away (New, Open, Generate).</summary>
		public static void Clear(Terrain t)
		{
			if (t == null) return;
			EditorSetup(t);
			TerrainData d = t.terrainData;
			d.SetDetailLayer(0, 0, 0, new int[d.detailHeight, d.detailWidth]);
		}

		/// <summary>Clear as one undo step (Generate): null when there was none.</summary>
		public static CommandUndoRedo.ICommand ClearWithUndo(Terrain t)
		{
			if (t == null) return null;
			EditorSetup(t);
			int[,] before = Layer(t);
			if (Count(before) == 0) return null;
			Clear(t);
			return new Command(t.terrainData, new RectInt(0, 0, before.GetLength(1), before.GetLength(0)), before);
		}

		/// <summary>The cells a world-space box covers on the editor's terrain.</summary>
		public static RectInt CellRect(Terrain t, Vector3 min, Vector3 max)
		{
			TerrainData d = t.terrainData;
			Vector3 o = t.transform.position;
			float cx = d.size.x / d.detailWidth, cz = d.size.z / d.detailHeight;
			int x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - o.x) / cx), 0, d.detailWidth - 1), z0 = Mathf.Clamp(Mathf.FloorToInt((min.z - o.z) / cz), 0, d.detailHeight - 1);
			int x1 = Mathf.Clamp(Mathf.CeilToInt((max.x - o.x) / cx), 0, d.detailWidth - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((max.z - o.z) / cz), 0, d.detailHeight - 1);
			return new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
		}

		static readonly System.Random brushRandom = new System.Random();

		/// <summary>
		/// One step of the brush: under water, each cell in the circle may gain a tuft (more near the middle and with more
		/// strength; up to MaxDensity); remove: the grass in the circle thins out the same way. Returns the cells changed.
		/// </summary>
		public static int Paint(Terrain t, Vector3 point, float radius, float strength, float deltaTime, bool remove, float waterY)
		{
			EditorSetup(t);
			TerrainData d = t.terrainData;
			Vector3 o = t.transform.position;
			RectInt r = CellRect(t, point - new Vector3(radius, 0, radius), point + new Vector3(radius, 0, radius));
			int[,] cells = d.GetDetailLayer(r.x, r.y, r.width, r.height, 0);
			float cx = d.size.x / d.detailWidth, cz = d.size.z / d.detailHeight;
			// (a brush smaller than a cell still reaches the nearest one)
			float reach = Mathf.Max(radius, Mathf.Max(cx, cz) * 0.75f);
			float chance = Mathf.Clamp01(strength * deltaTime * 0.5f);
			int changed = 0;
			for (int z = 0; z < r.height; z++)
				for (int x = 0; x < r.width; x++)
				{
					Vector3 c = new Vector3(o.x + (r.x + x + 0.5f) * cx, 0, o.z + (r.y + z + 0.5f) * cz);
					float dist = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(point.x, point.z));
					if (dist > reach) continue;
					float falloff = 1f - 0.6f * Mathf.Clamp01(dist / reach);
					if (brushRandom.NextDouble() > chance * falloff) continue;
					int v = cells[z, x];
					if (remove) { if (v > 0) { cells[z, x] = v - 1; changed++; } continue; }
					float ground = t.SampleHeight(c) + o.y;
					if (ground > waterY - MinDepth || v >= MaxDensity) continue;
					cells[z, x] = v + 1; changed++;
				}
			if (changed > 0) d.SetDetailLayer(r.x, r.y, 0, cells);
			return changed;
		}

		/// <summary>For the island file: the editor terrain's grass (null when there is none).</summary>
		public static byte[] Capture(Terrain t, out int resolution)
		{
			resolution = 0;
			TerrainData d = t.terrainData;
			if (d.detailPrototypes.Length == 0 || d.detailWidth != d.detailHeight) return null;
			int[,] layer = Layer(t);
			if (Count(layer) == 0) return null;
			resolution = d.detailWidth;
			var bytes = new byte[resolution * resolution];
			for (int z = 0; z < resolution; z++)
				for (int x = 0; x < resolution; x++)
					bytes[z * resolution + x] = (byte)Mathf.Clamp(layer[z, x], 0, 255);
			return bytes;
		}

		/// <summary>The editor shows an opened island's grass (or none).</summary>
		public static void Load(Terrain t, IslandFile island)
		{
			Clear(t);
			if (!island.HasGrass) return;
			TerrainData d = t.terrainData;
			int res = island.GrassResolution;
			if (res != d.detailWidth) Setup(t, res);
			var layer = new int[res, res];
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					layer[z, x] = island.GrassCells[z * res + x];
			d.SetDetailLayer(0, 0, 0, layer);
		}

		/// <summary>
		/// A spawned island's terrain (IslandSpawner.MakeRoot): the grass of its crop (heightmap samples x0, z0, size; the
		/// grass has a cell per heightmap cell) where the ground is under the sea. Returns the cells with grass.
		/// </summary>
		public static int Apply(Terrain t, IslandFile island, int x0, int z0, int size)
		{
			if (!island.HasGrass) return 0;
			int cellsAcross = island.HeightmapResolution - 1, res = island.GrassResolution, n = size - 1;
			if (res != cellsAcross || n <= 0) return 0;
			var layer = new int[n, n];
			int count = 0;
			for (int z = 0; z < n; z++)
				for (int x = 0; x < n; x++)
				{
					int gx = x0 + x, gz = z0 + z;
					if (gx < 0 || gz < 0 || gx >= res || gz >= res) continue;
					int v = island.GrassCells[gz * res + gx];
					if (v == 0) continue;
					// (under the sea: the cell's highest corner below the water level by MinDepth - ground raised after painting shows none)
					float top = Mathf.Max(Mathf.Max(island.Heights[gz, gx], island.Heights[gz, gx + 1]), Mathf.Max(island.Heights[gz + 1, gx], island.Heights[gz + 1, gx + 1])) * island.TerrainSize.y;
					if (top > island.WaterLevel - MinDepth) continue;
					layer[z, x] = v; count++;
				}
			if (count == 0) return 0;
			Setup(t, n);
			t.terrainData.SetDetailLayer(0, 0, 0, layer);
			return count;
		}

		/// <summary>The tail block: int32 resolution, then a byte per cell, row by row.</summary>
		public static byte[] Block(IslandFile island)
		{
			using (var m = new MemoryStream())
			using (var w = new BinaryWriter(m))
			{
				w.Write(island.GrassResolution);
				w.Write(island.GrassCells);
				w.Flush();
				return m.ToArray();
			}
		}

		/// <summary>Takes the "grass" block out of the island's tail into SeaGrass (wrong sizes: dropped).</summary>
		public static void Read(IslandFile island)
		{
			byte[] block;
			if (island.Tail == null || !island.Tail.TryGetValue(Tag, out block)) return;
			island.Tail.Remove(Tag);
			try
			{
				using (var r = new BinaryReader(new MemoryStream(block)))
				{
					int res = r.ReadInt32();
					if (res <= 0 || res > 4096 || block.Length != 4 + res * res) return;
					island.GrassResolution = res;
					island.GrassCells = r.ReadBytes(res * res);
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] " + island.Name + ": unreadable sea grass: " + e.Message); }
		}

		/// <summary>Undo/redo of a grass stroke: the cells of a rectangle before and after.</summary>
		public class Command : CommandUndoRedo.ICommand
		{
			readonly TerrainData data;
			readonly RectInt rect;
			readonly int[,] before, after;

			/// <summary>before: the rectangle's cells before (the terrain holds the after now).</summary>
			public Command(TerrainData data, RectInt rect, int[,] before)
			{
				this.data = data; this.rect = rect; this.before = before;
				after = data.GetDetailLayer(rect.x, rect.y, rect.width, rect.height, 0);
			}

			public void Execute() { Set(after); }
			public void UnExecute() { Set(before); }
			void Set(int[,] cells) { if (data != null && data.detailPrototypes.Length > 0) data.SetDetailLayer(rect.x, rect.y, 0, cells); }

			public static int[,] Crop(int[,] all, RectInt r)
			{
				var c = new int[r.height, r.width];
				for (int z = 0; z < r.height; z++)
					for (int x = 0; x < r.width; x++)
						c[z, x] = all[r.y + z, r.x + x];
				return c;
			}
		}
	}
}
