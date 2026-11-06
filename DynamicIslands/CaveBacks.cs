using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft draws its cave pieces' rock shells (Balboa's dens, the tunnels) from inside only: from outside - in the editor, or
	/// over a gap in the ground - and from inside wherever one fold of a shell is seen from behind, you looked through them (X7).
	/// Every cave piece gets its shell a second time, each face turned the other way: the same rock, seen from outside. Raft's
	/// meshes can't be read by the mod, so each is read back from the graphics card once (Unity 2021's mesh buffers).
	/// </summary>
	public static class CaveBacks
	{
		/// <summary>Name of the back's object (under each of the piece's meshes).</summary>
		public const string Name = "CaveBack";

		static readonly Dictionary<Mesh, Mesh> made = new Dictionary<Mesh, Mesh>();
		static bool warned;

		/// <summary>Selene's rooms, tunnels and doors (Raft's own walls, ceilings, floors and door frames inside the station):
		/// drawn from inside only too, so a room's outer wall - or its ceiling seen from above - showed the room through it (X7).</summary>
		public const string SelenePrefix = "TP_Selene_Int_";

		/// <summary>A piece that gets its backs: a cave piece's shell, or one of Selene's inside pieces.</summary>
		public static bool Wants(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			PropInfo p = RaftProps.Get(name);
			return (p != null && p.IsCave) || name.StartsWith(SelenePrefix, System.StringComparison.Ordinal);
		}

		/// <summary>The collider belongs to a piece drawn from both sides (its backs added): a look meeting its back sees it.</summary>
		public static bool DrawnBothSides(Collider c)
		{
			for (Transform t = c != null ? c.transform : null; t != null; t = t.parent)
				if (Wants(t.name.Replace("(Clone)", "").Trim())) return Has(t);
			return false;
		}

		/// <summary>A cave piece or a Selene piece just placed or spawned (editor or world): its faces get their other side.</summary>
		public static void Add(GameObject piece, string name)
		{
			if (piece == null || !Wants(name)) return;
			foreach (MeshFilter mf in piece.GetComponentsInChildren<MeshFilter>(true))
			{
				if (mf.name == Name || mf.name == CaveTool.MouthName || mf.name == CaveTool.JoinName || mf.sharedMesh == null || mf.transform.Find(Name) != null) continue;
				MeshRenderer r = mf.GetComponent<MeshRenderer>();
				if (r == null) continue;
				Mesh back = Back(mf.sharedMesh);
				if (back == null) continue;
				var go = new GameObject(Name);
				go.layer = mf.gameObject.layer;
				go.transform.SetParent(mf.transform, false);
				go.AddComponent<MeshFilter>().sharedMesh = back;
				MeshRenderer br = go.AddComponent<MeshRenderer>();
				br.sharedMaterials = r.sharedMaterials;
				br.shadowCastingMode = r.shadowCastingMode; // (so the roof shades the cave from the sun too: the inside's roof faces away from it)
				br.receiveShadows = r.receiveShadows;
				br.enabled = r.enabled;
			}
		}

		/// <summary>Tests (CIBackLook): 0 normals and tangents' handedness flipped (the backs as made), 1 faces turned only, 2 normals only.</summary>
		internal static int TestFlip;

		/// <summary>Tests: every back made again from its piece's mesh (after TestFlip changed); returns how many.</summary>
		internal static int Remake()
		{
			made.Clear();
			int n = 0;
			foreach (MeshFilter mf in Object.FindObjectsOfType<MeshFilter>())
			{
				MeshFilter front = mf.name == Name && mf.transform.parent != null ? mf.transform.parent.GetComponent<MeshFilter>() : null;
				if (front == null || front.sharedMesh == null) continue;
				Mesh b = Back(front.sharedMesh);
				if (b != null) { mf.sharedMesh = b; n++; }
			}
			return n;
		}

		/// <summary>The cave piece's meshes have their backs (tests).</summary>
		public static bool Has(Transform piece)
		{
			foreach (MeshFilter mf in piece.GetComponentsInChildren<MeshFilter>(true))
				if (mf.name != Name && mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null && mf.transform.Find(Name) == null) return false;
			return true;
		}

		/// <summary>The mesh turned inside out: same points, faces wound the other way, normals reversed. Null: couldn't be read.</summary>
		static Mesh Back(Mesh m)
		{
			Mesh back;
			if (made.TryGetValue(m, out back)) return back;
			back = null;
			try
			{
				VertexAttributeDescriptor[] attrs = m.GetVertexAttributes();
				int count = m.vertexCount;
				var streams = new byte[m.vertexBufferCount][];
				for (int s = 0; s < streams.Length; s++)
					using (GraphicsBuffer vb = m.GetVertexBuffer(s))
					{
						streams[s] = new byte[vb.count * vb.stride];
						vb.GetData(streams[s]);
					}
				// (the normals reversed; and the tangents' handedness, so a bumpy surface stays bumped the same way)
				if (TestFlip != 1) Flip(m, streams, VertexAttribute.Normal, 3);
				if (TestFlip == 0) Flip(m, streams, VertexAttribute.Tangent, 1);
				byte[] index;
				using (GraphicsBuffer ib = m.GetIndexBuffer())
				{
					index = new byte[ib.count * ib.stride];
					ib.GetData(index);
				}
				int size = m.indexFormat == IndexFormat.UInt16 ? 2 : 4;
				for (int sm = 0; sm < m.subMeshCount; sm++)
				{
					SubMeshDescriptor d = m.GetSubMesh(sm);
					if (d.topology != MeshTopology.Triangles) continue;
					for (int i = d.indexStart; i + 2 < d.indexStart + d.indexCount; i += 3)
						for (int b = 0; b < size; b++)
						{
							byte t = index[(i + 1) * size + b];
							index[(i + 1) * size + b] = index[(i + 2) * size + b];
							index[(i + 2) * size + b] = t;
						}
				}
				back = new Mesh { name = m.name + " (back)" };
				back.SetVertexBufferParams(count, attrs);
				for (int s = 0; s < streams.Length; s++) back.SetVertexBufferData(streams[s], 0, 0, streams[s].Length, s, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
				back.SetIndexBufferParams(index.Length / size, m.indexFormat);
				back.SetIndexBufferData(index, 0, 0, index.Length, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
				back.subMeshCount = m.subMeshCount;
				for (int sm = 0; sm < m.subMeshCount; sm++) back.SetSubMesh(sm, m.GetSubMesh(sm), MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
				back.bounds = m.bounds;
				back.UploadMeshData(true);
			}
			catch (System.Exception e)
			{
				back = null;
				if (!warned) Debug.LogWarning("[CUSTOM ISLANDS] A cave piece's outside couldn't be made (" + m.name + "): " + e.Message);
				warned = true;
			}
			made[m] = back;
			return back;
		}

		/// <summary>Negates the first n components of an attribute in the raw vertex data, whatever number format it is in.</summary>
		static void Flip(Mesh m, byte[][] streams, VertexAttribute a, int n)
		{
			if (!m.HasVertexAttribute(a)) return;
			int s = m.GetVertexAttributeStream(a), offset = m.GetVertexAttributeOffset(a), stride = m.GetVertexBufferStride(s), dim = m.GetVertexAttributeDimension(a);
			VertexAttributeFormat f = m.GetVertexAttributeFormat(a);
			byte[] data = streams[s];
			int first = a == VertexAttribute.Tangent ? 3 : 0, last = a == VertexAttribute.Tangent ? Mathf.Min(dim, 4) : Mathf.Min(dim, n); // (a tangent: just its w)
			if (a == VertexAttribute.Tangent && dim < 4) return;
			for (int v = 0; v * stride + offset < data.Length; v++)
				for (int c = first; c < last; c++)
				{
					switch (f)
					{
						case VertexAttributeFormat.Float32:
						{
							int at = v * stride + offset + c * 4;
							data[at + 3] ^= 0x80; // (the sign bit: little-endian, the last byte)
							break;
						}
						case VertexAttributeFormat.Float16:
						{
							int at = v * stride + offset + c * 2;
							data[at + 1] ^= 0x80;
							break;
						}
						case VertexAttributeFormat.SNorm8:
						{
							int at = v * stride + offset + c;
							data[at] = (byte)(sbyte)Mathf.Max(-127, -(sbyte)data[at]);
							break;
						}
						case VertexAttributeFormat.SNorm16:
						{
							int at = v * stride + offset + c * 2;
							short x = (short)(data[at] | (data[at + 1] << 8));
							x = (short)Mathf.Max(-32767, -x);
							data[at] = (byte)(x & 0xff);
							data[at + 1] = (byte)((x >> 8) & 0xff);
							break;
						}
					}
				}
		}
	}
	/// <summary>
	/// Balboa's rock shader ("Mountains": Raft's big rocks, its cave pieces and the cave hills made from them) draws its steep faces
	/// with Rock_Stylized - a grey-blue stone. Out of Balboa's own light, on a custom island's caves and hills, those faces read as
	/// bright blue patterned patches, not mountain rock (11-06 user: Old Mine Islet's den and mine mouth). Every such piece wears a
	/// copy of its material (one per material, shared) with that texture turned brown-grey: its own pattern, the island rock's hue.
	/// Raft's own Balboa keeps its materials.
	/// </summary>
	public static class CaveRock
	{
		const string Prop = "_RockDiffuse";
		/// <summary>The hue the steep rock takes (its brightness kept): Raft's brown-grey island rock.</summary>
		internal static Color Hue = new Color(1f, 0.85f, 0.68f);
		static readonly Dictionary<Material, Material> warmed = new Dictionary<Material, Material>();
		static readonly Dictionary<Material, Material> original = new Dictionary<Material, Material>();
		/// <summary>Tests: the last texture's average colour before and the factor it was multiplied by.</summary>
		internal static string LastNote = "";

		/// <summary>The material with its steep rock warmed (the same material when it has none, or is already a warmed copy).</summary>
		public static Material Warmed(Material m)
		{
			if (m == null || original.ContainsKey(m) || !m.HasProperty(Prop)) return m;
			Material w;
			if (warmed.TryGetValue(m, out w) && w != null) return w;
			Texture2D t = m.GetTexture(Prop) as Texture2D;
			if (t == null) return m;
			Color avg = Average(t);
			// (each channel scaled so the average lands on Hue's balance; the strongest channel kept, the others lowered)
			Color f = new Color(Hue.r / Mathf.Max(avg.r, 0.01f), Hue.g / Mathf.Max(avg.g, 0.01f), Hue.b / Mathf.Max(avg.b, 0.01f));
			float top = Mathf.Max(f.r, Mathf.Max(f.g, f.b));
			f = new Color(f.r / top, f.g / top, f.b / top, 1f);
			w = new Material(m) { name = m.name };
			w.SetTexture(Prop, TerrainPainter.Tinted(t, f));
			warmed[m] = w;
			original[w] = m;
			LastNote = t.name + " average " + avg + ", factor " + f;
			Debug.Log("[CUSTOM ISLANDS] Cave rock " + m.name + ": " + LastNote);
			return w;
		}

		/// <summary>Every renderer under the object: its Balboa rock materials swapped for the warmed copies. Returns how many renderers.</summary>
		public static int Apply(GameObject go)
		{
			if (go == null) return 0;
			int n = 0;
			foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
			{
				if (r is ParticleSystemRenderer) continue;
				Material[] mats = r.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < mats.Length; i++)
				{
					Material w = Warmed(mats[i]);
					if (w != mats[i]) { mats[i] = w; changed = true; }
				}
				if (changed) { r.sharedMaterials = mats; n++; }
			}
			return n;
		}

		/// <summary>Tests (CIRockLook cavehue): every warmed copy made again with another hue; renderers re-pointed. Returns how many.</summary>
		internal static int Remake(Color hue)
		{
			Hue = hue;
			var swap = new Dictionary<Material, Material>();
			foreach (KeyValuePair<Material, Material> p in warmed) swap[p.Value] = p.Key;
			warmed.Clear();
			original.Clear();
			int n = 0;
			foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
			{
				Material[] mats = r.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < mats.Length; i++)
				{
					Material o;
					if (mats[i] != null && swap.TryGetValue(mats[i], out o)) { mats[i] = Warmed(o); changed = true; }
				}
				if (changed) { r.sharedMaterials = mats; n++; }
			}
			return n;
		}

		/// <summary>A texture's average colour (read back small from the graphics card: Raft's textures can't be read).</summary>
		static Color Average(Texture t)
		{
			var rt = RenderTexture.GetTemporary(32, 32, 0, RenderTextureFormat.ARGB32);
			RenderTexture previous = RenderTexture.active;
			try
			{
				Graphics.Blit(t, rt);
				RenderTexture.active = rt;
				var small = new Texture2D(32, 32, TextureFormat.RGBA32, false);
				small.ReadPixels(new Rect(0, 0, 32, 32), 0, 0);
				Color sum = Color.black;
				foreach (Color c in small.GetPixels()) sum += c;
				Object.Destroy(small);
				return sum / 1024f;
			}
			catch { return new Color(0.5f, 0.5f, 0.5f); }
			finally
			{
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(rt);
			}
		}
	}
}
