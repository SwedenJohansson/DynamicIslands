using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Light in the editor (the original roadmap's "good lighting in the editor"): the time of day - Morning, Noon, Evening,
	/// Night, Overcast - sets the sun (the scene's directional light, or one of the mod's own), its colour and shadows, the
	/// sky's light on everything (ambient) and a light haze, so an island can be looked at as it will be in Raft at that
	/// time. The Light button in the top bar goes through them; the choice is kept (Mods\DynamicIslands\editor_light.txt).
	/// </summary>
	public static class EditorLighting
	{
		public static readonly string[] Names = { "Morning", "Noon", "Evening", "Night", "Overcast" };

		class Preset
		{
			public float Height, Turn, Intensity, Shadow, Fog;
			public Color Sun, Sky, Equator, Ground, Haze;
		}

		static readonly Preset[] presets =
		{
			new Preset { Height = 18f, Turn = 80f, Intensity = 0.95f, Shadow = 0.75f, Sun = new Color(1f, 0.82f, 0.62f), Sky = new Color(0.62f, 0.66f, 0.74f), Equator = new Color(0.62f, 0.56f, 0.5f), Ground = new Color(0.3f, 0.27f, 0.24f), Haze = new Color(0.86f, 0.8f, 0.72f), Fog = 0.0009f },
			new Preset { Height = 58f, Turn = 150f, Intensity = 1.15f, Shadow = 0.8f, Sun = new Color(1f, 0.97f, 0.9f), Sky = new Color(0.62f, 0.74f, 0.9f), Equator = new Color(0.6f, 0.64f, 0.66f), Ground = new Color(0.34f, 0.32f, 0.28f), Haze = new Color(0.74f, 0.84f, 0.92f), Fog = 0.0006f },
			new Preset { Height = 12f, Turn = 250f, Intensity = 0.85f, Shadow = 0.7f, Sun = new Color(1f, 0.62f, 0.38f), Sky = new Color(0.55f, 0.5f, 0.62f), Equator = new Color(0.66f, 0.46f, 0.38f), Ground = new Color(0.26f, 0.2f, 0.18f), Haze = new Color(0.9f, 0.62f, 0.48f), Fog = 0.0011f },
			new Preset { Height = 40f, Turn = 200f, Intensity = 0.28f, Shadow = 0.5f, Sun = new Color(0.62f, 0.72f, 1f), Sky = new Color(0.12f, 0.16f, 0.28f), Equator = new Color(0.1f, 0.12f, 0.2f), Ground = new Color(0.05f, 0.05f, 0.08f), Haze = new Color(0.08f, 0.1f, 0.18f), Fog = 0.0012f },
			new Preset { Height = 75f, Turn = 150f, Intensity = 0.6f, Shadow = 0.3f, Sun = new Color(0.9f, 0.92f, 0.95f), Sky = new Color(0.7f, 0.72f, 0.76f), Equator = new Color(0.64f, 0.65f, 0.67f), Ground = new Color(0.36f, 0.36f, 0.36f), Haze = new Color(0.72f, 0.74f, 0.78f), Fog = 0.0016f },
		};

		/// <summary>The time of day shown now (an index into Names).</summary>
		public static int Current { get; private set; }
		static Light sun;

		static string FilePath { get { return Path.Combine(DynamicIslands.assetpath, "editor_light.txt"); } }

		/// <summary>The editor has opened: the kept time of day (Noon the first time).</summary>
		public static void OnEditorOpened()
		{
			int i = 1;
			try { if (File.Exists(FilePath)) i = Array.FindIndex(Names, n => n.Equals(File.ReadAllText(FilePath).Trim(), StringComparison.OrdinalIgnoreCase)); } catch { }
			Apply(i < 0 ? 1 : i, false);
		}

		/// <summary>The top bar's Light button: the next time of day.</summary>
		public static void Next() { Apply((Current + 1) % Names.Length, true); }

		public static void Apply(int index, bool keep)
		{
			Current = Mathf.Clamp(index, 0, Names.Length - 1);
			Preset p = presets[Current];
			if (sun == null)
			{
				// (the editor scene's own sun if it has one; else one of the mod's)
				sun = UnityEngine.Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional && l.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene());
				if (sun == null)
				{
					var go = new GameObject("CustomIslands_EditorSun");
					sun = go.AddComponent<Light>();
					sun.type = LightType.Directional;
				}
			}
			sun.transform.rotation = Quaternion.Euler(p.Height, p.Turn, 0f);
			sun.color = p.Sun;
			sun.intensity = p.Intensity;
			sun.shadows = LightShadows.Soft;
			sun.shadowStrength = p.Shadow;
			RenderSettings.sun = sun;
			RenderSettings.ambientMode = AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = p.Sky;
			RenderSettings.ambientEquatorColor = p.Equator;
			RenderSettings.ambientGroundColor = p.Ground;
			RenderSettings.fog = true;
			RenderSettings.fogMode = FogMode.ExponentialSquared;
			RenderSettings.fogColor = p.Haze;
			RenderSettings.fogDensity = p.Fog;
			Camera cam = Camera.main;
			if (cam != null && cam.clearFlags != CameraClearFlags.Skybox) cam.backgroundColor = p.Haze;
			if (keep)
			{
				try { File.WriteAllText(FilePath, Names[Current]); } catch { }
				DynamicIslands.Notify("Light: " + Names[Current] + " (how the island looks at that time of day; only in the editor)");
			}
		}
	}
}
