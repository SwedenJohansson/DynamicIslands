using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Small preview pictures of catalog objects for the editor's object browser. Each object is rendered once, when
	/// its tile first becomes visible: a copy is put far away on its own layer, a hidden camera frames it, and the
	/// picture is kept in a RenderTexture. A few per frame, so scrolling stays smooth.
	/// </summary>
	public class ObjectThumbnails : MonoBehaviour
	{
		const int Size = 128;
		const int PerFrame = 3;
		/// <summary>A layer Raft doesn't use for anything we see; the camera renders only this layer.</summary>
		const int Layer = 31;
		static readonly Vector3 Stage = new Vector3(-30000f, -30000f, -30000f);
		static readonly Color Backdrop = new Color(0.16f, 0.19f, 0.23f, 1f);

		static ObjectThumbnails instance;
		static readonly Dictionary<string, RenderTexture> done = new Dictionary<string, RenderTexture>();
		readonly List<KeyValuePair<string, RawImage>> queue = new List<KeyValuePair<string, RawImage>>();
		readonly Dictionary<string, int> attempts = new Dictionary<string, int>();
		Texture2D probe;
		Camera cam;
		Light key, fill;

		/// <summary>Shows the object's picture in <paramref name="target"/>, now if it exists, else once it is rendered.</summary>
		public static void Request(string name, RawImage target)
		{
			RenderTexture rt;
			if (done.TryGetValue(name, out rt) && rt != null && rt.IsCreated()) { Show(target, rt); return; }
			if (instance == null)
			{
				var go = new GameObject("CustomIslands_Thumbnails");
				instance = go.AddComponent<ObjectThumbnails>();
			}
			instance.queue.RemoveAll(p => p.Value == target);
			instance.queue.Add(new KeyValuePair<string, RawImage>(name, target));
		}

		/// <summary>Drops a picture so it is rendered again (the object changed, e.g. a creature marker became its real model).</summary>
		public static void Forget(string name)
		{
			RenderTexture rt;
			if (done.TryGetValue(name, out rt) && rt != null) { rt.Release(); Destroy(rt); }
			done.Remove(name);
		}

		public static bool Has(string name)
		{
			RenderTexture rt;
			return done.TryGetValue(name, out rt) && rt != null && rt.IsCreated();
		}

		static void Show(RawImage target, RenderTexture rt)
		{
			if (target == null) return;
			target.texture = rt;
			target.color = Color.white;
		}

		void Awake()
		{
			cam = gameObject.AddComponent<Camera>();
			cam.enabled = false;
			cam.cullingMask = 1 << Layer;
			cam.clearFlags = CameraClearFlags.SolidColor;
			cam.backgroundColor = Backdrop;
			cam.fieldOfView = 30f;
			cam.nearClipPlane = 0.05f;
			cam.farClipPlane = 2000f;
			cam.allowHDR = false;
			cam.allowMSAA = true;
			// The stage is far outside the level: Raft's occlusion data would cull objects there at random
			cam.useOcclusionCulling = false;
			// Its own lights, so the pictures look the same whatever the editor's lighting (and only on this layer)
			key = MakeLight("Key", new Vector3(40f, 135f, 0f), 1.1f);
			fill = MakeLight("Fill", new Vector3(20f, -60f, 0f), 0.45f);
		}

		Light MakeLight(string name, Vector3 euler, float intensity)
		{
			var go = new GameObject(name);
			go.transform.SetParent(transform, false);
			go.transform.rotation = Quaternion.Euler(euler);
			Light l = go.AddComponent<Light>();
			l.type = LightType.Directional;
			l.intensity = intensity;
			l.color = new Color(1f, 0.97f, 0.92f);
			l.cullingMask = 1 << Layer;
			l.shadows = LightShadows.None;
			l.enabled = false;
			return l;
		}

		void LateUpdate()
		{
			int rendered = 0;
			while (queue.Count > 0 && rendered < PerFrame)
			{
				KeyValuePair<string, RawImage> job = queue[0];
				queue.RemoveAt(0);
				// Tiles that were scrolled out of view or destroyed are skipped; they ask again when shown
				if (job.Value == null || !job.Value.isActiveAndEnabled) continue;
				RenderTexture rt;
				if (!done.TryGetValue(job.Key, out rt) || rt == null || !rt.IsCreated())
				{
					rt = Render(job.Key);
					if (rt == null) continue;
					rendered++;
					// A picture that came out empty is tried again next frame (a couple of times)
					int tries;
					attempts.TryGetValue(job.Key, out tries);
					if (IsBlank(rt) && tries < 3)
					{
						attempts[job.Key] = tries + 1;
						rt.Release(); Destroy(rt);
						queue.Add(job);
						break;
					}
					done[job.Key] = rt;
				}
				Show(job.Value, rt);
			}
		}

		/// <summary>True if the picture is only the backdrop (checked on a coarse grid of pixels).</summary>
		bool IsBlank(RenderTexture rt)
		{
			if (probe == null) probe = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = rt;
			probe.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
			RenderTexture.active = previous;
			Color32[] px = probe.GetPixels32();
			Color32 bg = Backdrop;
			for (int i = 0; i < px.Length; i += 37)
				if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > 12) return false;
			return true;
		}

		RenderTexture Render(string name)
		{
			GameObject go = PlaceableCatalog.Spawn(name, null);
			if (go == null) return null;
			try
			{
				go.transform.position = Stage;
				foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
				// The picture shows the full-detail model
				foreach (LODGroup g in go.GetComponentsInChildren<LODGroup>()) g.ForceLOD(0);
				Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
				Bounds b = new Bounds(Stage, Vector3.one);
				bool any = false;
				foreach (Renderer r in renderers)
				{
					if (r.name == ContentCatalog.MarkerOnly) { r.enabled = false; continue; } // name tags and ground rings
					if (!r.enabled || r is ParticleSystemRenderer) continue;
					if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
				}
				float radius = Mathf.Max(b.extents.magnitude, 0.1f);
				Vector3 dir = new Vector3(0.9f, 0.65f, -1f).normalized;
				float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
				cam.transform.position = b.center + dir * distance;
				cam.transform.LookAt(b.center);
				cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 1.5f);
				cam.farClipPlane = distance + radius * 1.5f;

				var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { name = "CI_Thumb_" + name, antiAliasing = 4 };
				rt.Create();
				cam.targetTexture = rt;
				key.enabled = fill.enabled = true;
				// Raft's fog would grey out the far side of big objects
				bool fog = RenderSettings.fog;
				RenderSettings.fog = false;
				cam.Render();
				RenderSettings.fog = fog;
				key.enabled = fill.enabled = false;
				cam.targetTexture = null;
				return rt;
			}
			catch (System.Exception e)
			{
				Debug.LogWarning("[CUSTOM ISLANDS] Thumbnail for " + name + ": " + e.Message);
				return null;
			}
			finally
			{
				go.SetActive(false);
				Destroy(go);
			}
		}
	}
}
