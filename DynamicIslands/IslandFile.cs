using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// One object placed on an island. Position/rotation/scale are local to the island root.
	/// </summary>
	public class IslandObject
	{
		public string Name;
		public Vector3 Position;
		public Vector3 EulerRotation;
		public Vector3 Scale = Vector3.one;
		/// <summary>
		/// Extra data of this object (format 4), or null: what makes it a creature spawn point or a readable note, its
		/// tint... Keys and values are plain strings, see ObjectProps.
		/// </summary>
		public Dictionary<string, string> Props;
	}

	/// <summary>
	/// A saved island: terrain heightmap + placed objects.
	///
	/// File layout (.island):
	///   4 bytes  magic "CISL"
	///   int32    format version
	///   rest     deflate-compressed body:
	///     string   island name
	///     float    water level (editor Y that becomes sea level in game)
	///     vector3  terrain size
	///     int32    heightmap resolution (N)
	///     uint16[N*N] heights, row-major [y, x], 0..65535 = 0..1
	///     int32    object count, then per object: string name, vector3 pos, vector3 euler, vector3 scale
	///   version 2 adds, after the objects:
	///     bool     has texture paint; if true:
	///       int32  alphamap resolution (R), int32 layer count (L)
	///       byte[L*R*R] layer weights, layer-major then row-major [z, x], 0..255
	///       bool   has paint mask; if true: byte[R*R] (255 = painted by hand, protected from auto texturing)
	///   version 3 adds, after the paint:
	///     float    elevation: metres above sea level the island floats at in game (negative = under water).
	///     string   style ("" = tropical; "Snowy", "Desert", "Forest", "Volcanic")
	///              Normal tropical islands (elevation 0, no style) are still written as version 2 so older versions
	///              of the mod can read them.
	///   version 4 adds, after the style (only written when the island or one of its objects has properties):
	///     int32    island property count, then per property: string key, string value (IslandProps: author, description...)
	///     int32    count of objects with properties, then per object: int32 index in the object list above,
	///              int32 property count, then per property: string key, string value (creatures, notes, tints: ObjectProps)
	/// </summary>
	public class IslandFile
	{
		public const string Extension = ".island";
		const uint Magic = 0x4C534943; // "CISL" little-endian
		const int FormatVersion = 4;

		/// <summary>Editor Y coordinate that is treated as sea level when spawned in game.</summary>
		public const float DefaultWaterLevel = 20f;
		/// <summary>
		/// Water level of islands generated on a deep sea floor like Raft's own: its islands rise from a floor about
		/// 150-165 m down (measured by CIMeasureUnderwater), so the terrain's base is that far below the sea.
		/// </summary>
		public const float DeepWaterLevel = 160f;

		public string Name = "";
		public float WaterLevel = DefaultWaterLevel;
		public Vector3 TerrainSize;
		public int HeightmapResolution;
		public float[,] Heights; // [y, x], 0..1, same layout as TerrainData.GetHeights
		public List<IslandObject> Objects = new List<IslandObject>();

		/// <summary>Texture paint (null when the island only uses automatic texturing, e.g. version 1 files).</summary>
		public int AlphamapResolution;
		public int AlphamapLayers;
		public byte[] Alphamaps;   // [layer][z][x]
		public byte[] PaintMask;   // [z][x], may be null

		/// <summary>Metres above sea level the island floats at in game (negative = under water, 0 = a normal island).</summary>
		public float Elevation;

		/// <summary>Island style (TerrainPainter.Styles: "Snowy", "Desert"...); empty = tropical.</summary>
		public string Style = "";

		/// <summary>Island-wide settings (format 4), see IslandProps for the keys.</summary>
		public Dictionary<string, string> Props = new Dictionary<string, string>();

		bool NeedsFormat4 { get { return Props.Count > 0 || Objects.Any(o => o.Props != null && o.Props.Count > 0); } }

		bool NeedsFormat3 { get { return Elevation != 0f || (!string.IsNullOrEmpty(Style) && TerrainPainter.StyleIndex(Style) != TerrainPainter.Tropical); } }

		public bool HasPaint { get { return Alphamaps != null && AlphamapResolution > 0 && AlphamapLayers > 0; } }

		public void Save(string path)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			string tmp = path + ".tmp";
			using (var file = File.Create(tmp))
			{
				var header = new BinaryWriter(file);
				header.Write(Magic);
				// Each file uses the oldest format that holds what it needs, so simple islands stay readable by older versions of the mod
				bool v4 = NeedsFormat4, v3 = v4 || NeedsFormat3;
				header.Write(v4 ? 4 : v3 ? 3 : 2);
				header.Flush();

				using (var deflate = new DeflaterOutputStream(file) { IsStreamOwner = false })
				using (var w = new BinaryWriter(deflate))
				{
					w.Write(Name ?? "");
					w.Write(WaterLevel);
					WriteVector(w, TerrainSize);
					w.Write(HeightmapResolution);
					for (int y = 0; y < HeightmapResolution; y++)
						for (int x = 0; x < HeightmapResolution; x++)
							w.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01(Heights[y, x]) * ushort.MaxValue));

					w.Write(Objects.Count);
					foreach (IslandObject o in Objects)
					{
						w.Write(o.Name ?? "");
						WriteVector(w, o.Position);
						WriteVector(w, o.EulerRotation);
						WriteVector(w, o.Scale);
					}

					w.Write(HasPaint);
					if (HasPaint)
					{
						w.Write(AlphamapResolution);
						w.Write(AlphamapLayers);
						w.Write(Alphamaps);
						w.Write(PaintMask != null);
						if (PaintMask != null) w.Write(PaintMask);
					}
					if (v3) { w.Write(Elevation); w.Write(Style ?? ""); }
					if (v4)
					{
						WriteProps(w, Props);
						List<int> withProps = Enumerable.Range(0, Objects.Count).Where(i => Objects[i].Props != null && Objects[i].Props.Count > 0).ToList();
						w.Write(withProps.Count);
						foreach (int i in withProps) { w.Write(i); WriteProps(w, Objects[i].Props); }
					}
					w.Flush();
					deflate.Finish();
				}
			}
			// Replace atomically so a crash mid-save never destroys the previous file
			if (File.Exists(path)) File.Delete(path);
			File.Move(tmp, path);
		}

		public static IslandFile Load(string path)
		{
			using (var file = File.OpenRead(path))
			{
				var header = new BinaryReader(file);
				if (header.ReadUInt32() != Magic)
					throw new InvalidDataException(path + " is not a Custom Islands .island file");
				int version = header.ReadInt32();
				if (version > FormatVersion)
					throw new InvalidDataException(path + " was saved by a newer version of Custom Islands (format " + version + ")");

				using (var inflate = new InflaterInputStream(file) { IsStreamOwner = false })
				using (var r = new BinaryReader(inflate))
				{
					var island = new IslandFile();
					island.Name = r.ReadString();
					island.WaterLevel = r.ReadSingle();
					island.TerrainSize = ReadVector(r);
					int res = r.ReadInt32();
					if (res <= 0 || res > 4097)
						throw new InvalidDataException("Invalid heightmap resolution " + res);
					island.HeightmapResolution = res;
					island.Heights = new float[res, res];
					for (int y = 0; y < res; y++)
						for (int x = 0; x < res; x++)
							island.Heights[y, x] = r.ReadUInt16() / (float)ushort.MaxValue;

					int count = r.ReadInt32();
					for (int i = 0; i < count; i++)
					{
						island.Objects.Add(new IslandObject
						{
							Name = r.ReadString(),
							Position = ReadVector(r),
							EulerRotation = ReadVector(r),
							Scale = ReadVector(r),
						});
					}

					if (version >= 2 && r.ReadBoolean())
					{
						int ares = r.ReadInt32(), layers = r.ReadInt32();
						if (ares < 16 || ares > 4096 || layers < 1 || layers > 16)
							throw new InvalidDataException("Invalid texture paint data (" + ares + " px, " + layers + " layers)");
						island.AlphamapResolution = ares;
						island.AlphamapLayers = layers;
						island.Alphamaps = ReadExactly(r, layers * ares * ares);
						if (r.ReadBoolean()) island.PaintMask = ReadExactly(r, ares * ares);
					}
					if (version >= 3)
					{
						island.Elevation = r.ReadSingle();
						// The first format 3 files (flying islands, before styles) end after the elevation
						try { island.Style = r.ReadString(); } catch (EndOfStreamException) { }
					}
					if (version >= 4)
					{
						island.Props = ReadProps(r);
						int withProps = r.ReadInt32();
						if (withProps < 0 || withProps > island.Objects.Count) throw new InvalidDataException("Invalid object property count " + withProps);
						for (int n = 0; n < withProps; n++)
						{
							int i = r.ReadInt32();
							Dictionary<string, string> props = ReadProps(r);
							if (i >= 0 && i < island.Objects.Count) island.Objects[i].Props = props;
						}
					}
					return island;
				}
			}
		}

		/// <summary>Captures terrain + every EditorGameObject under placedObjectsRoot.</summary>
		public static IslandFile Capture(string name, Terrain terrain, Transform placedObjectsRoot, float[,] paintMask = null)
		{
			// (the sea level of the island being edited: shallow seabed or Raft's deep sea floor)
			var island = new IslandFile { Name = name, WaterLevel = DynamicIslands.EditorWaterLevel };
			TerrainData data = terrain.terrainData;
			island.TerrainSize = data.size;
			island.HeightmapResolution = data.heightmapResolution;
			island.Heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);

			if (data.alphamapLayers > 0)
			{
				int res = data.alphamapResolution, layers = data.alphamapLayers;
				float[,,] maps = data.GetAlphamaps(0, 0, res, res);
				island.AlphamapResolution = res;
				island.AlphamapLayers = layers;
				island.Alphamaps = new byte[layers * res * res];
				for (int l = 0; l < layers; l++)
					for (int z = 0; z < res; z++)
						for (int x = 0; x < res; x++)
							island.Alphamaps[(l * res + z) * res + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(maps[z, x, l]) * 255f);
				if (paintMask != null && paintMask.GetLength(0) == res && paintMask.GetLength(1) == res)
				{
					island.PaintMask = new byte[res * res];
					for (int z = 0; z < res; z++)
						for (int x = 0; x < res; x++)
							island.PaintMask[z * res + x] = paintMask[z, x] > 0.5f ? (byte)255 : (byte)0;
				}
			}

			foreach (EditorGameObject ego in placedObjectsRoot.GetComponentsInChildren<EditorGameObject>())
			{
				Transform t = ego.transform;
				island.Objects.Add(new IslandObject
				{
					Name = ego.GameObjectName,
					Props = ego.Props != null && ego.Props.Count > 0 ? new Dictionary<string, string>(ego.Props) : null,
					// Store relative to the terrain so the island can be spawned anywhere
					Position = t.position - terrain.transform.position,
					EulerRotation = t.rotation.eulerAngles,
					Scale = t.lossyScale,
				});
			}
			return island;
		}

		/// <summary>
		/// Texture weights for a square block of the alphamap, normalised so the layers at each pixel sum to 1.
		/// Returns [z, x, layer] as Unity's SetAlphamaps expects.
		/// </summary>
		public float[,,] GetAlphamapBlock(int x0, int z0, int size)
		{
			int res = AlphamapResolution, layers = AlphamapLayers;
			var maps = new float[size, size, layers];
			for (int z = 0; z < size; z++)
				for (int x = 0; x < size; x++)
				{
					float sum = 0;
					for (int l = 0; l < layers; l++) sum += Alphamaps[(l * res + z0 + z) * res + x0 + x];
					for (int l = 0; l < layers; l++)
						maps[z, x, l] = sum > 0 ? Alphamaps[(l * res + z0 + z) * res + x0 + x] / sum : (l == 0 ? 1f : 0f);
				}
			return maps;
		}

		/// <summary>The paint mask as floats (1 = painted by hand), or null.</summary>
		public float[,] GetPaintMask()
		{
			if (PaintMask == null) return null;
			int res = AlphamapResolution;
			var mask = new float[res, res];
			for (int z = 0; z < res; z++)
				for (int x = 0; x < res; x++)
					mask[z, x] = PaintMask[z * res + x] / 255f;
			return mask;
		}

		static byte[] ReadExactly(BinaryReader r, int count)
		{
			byte[] b = r.ReadBytes(count);
			if (b.Length != count) throw new InvalidDataException("Island file is truncated");
			return b;
		}

		static void WriteProps(BinaryWriter w, Dictionary<string, string> props)
		{
			w.Write(props.Count);
			foreach (var kv in props.OrderBy(k => k.Key, StringComparer.Ordinal)) { w.Write(kv.Key ?? ""); w.Write(kv.Value ?? ""); }
		}

		static Dictionary<string, string> ReadProps(BinaryReader r)
		{
			int n = r.ReadInt32();
			if (n < 0 || n > 10000) throw new InvalidDataException("Invalid property count " + n);
			var props = new Dictionary<string, string>();
			for (int i = 0; i < n; i++) { string k = r.ReadString(); props[k] = r.ReadString(); }
			return props;
		}

		static void WriteVector(BinaryWriter w, Vector3 v)
		{
			w.Write(v.x); w.Write(v.y); w.Write(v.z);
		}

		static Vector3 ReadVector(BinaryReader r)
		{
			return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		}
	}
}
