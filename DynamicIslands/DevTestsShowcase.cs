using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;

namespace DynamicIslands
{
	/// <summary>
	/// The community showcase video (11-18): frames recorded inside Raft, so the Raft can stay minimised and muted and the
	/// mouse is never touched. CIRecord writes numbered 1920x1080 JPGs with Time.captureFramerate fixed, so the game steps
	/// exactly 1/fps per frame however slowly the frames are written. CIShowcase drives the scripted parts (editor,
	/// New Game box, islands in a world) with an eased camera path.
	/// </summary>
	public static partial class DevTests
	{
		#region Recorder (CIRecord)

		const int RecW = 1920, RecH = 1080;
		static bool recOn, recPaused, recUi, recScreen, recGfx;
		static string recDir;
		static int recFps = 30, recFrame, recPending;
		static RenderTexture recRt;
		static Texture2D recTex;
		static Camera recUiCam;
		/// <summary>The camera's pose for the frame (null: the camera where the game put it); t = seconds of recorded video.</summary>
		static Func<float, Pose?> recPose;
		static float recPoseStart;
		static readonly FieldInfo recWaterFx = typeof(UltimateWater.WaterCamera).GetField("_ImageEffects", BindingFlags.Instance | BindingFlags.NonPublic);
		static readonly List<RecCanvas> recCanvases = new List<RecCanvas>();
		// (v3 11-19: less shimmer on leaves) recSs: the camera drawn at 2x (3840x2160) and halved - a bilinear blit at exactly
		// half size is a 2x2 box filter; recSub: sub-frames per video frame (the clock at fps x recSub), averaged - a light motion
		// blur that evens out what still flickers. Either on: anti-aliasing (FXAA, extreme), lodBias x3 and anisotropic textures
		// forced for the recording, then the user's values back.
		static int recSs = 1, recSub = 1, recSubIdx;
		static RenderTexture recBig;
		static int[] recAcc;
		static bool recHq;
		static float recHqLod;
		static AnisotropicFiltering recHqAniso;
		static readonly List<KeyValuePair<UnityEngine.PostProcessing.AntialiasingModel, KeyValuePair<bool, UnityEngine.PostProcessing.AntialiasingModel.Settings>>> recHqAa = new List<KeyValuePair<UnityEngine.PostProcessing.AntialiasingModel, KeyValuePair<bool, UnityEngine.PostProcessing.AntialiasingModel.Settings>>>();
		/// <summary>The showcase island shots' quality (CIShowcase quality ss sub): 2x supersampled, 4 sub-frames.</summary>
		static int scSs = 2, scSub = 4;

		struct Pose { public Vector3 Pos, Look; public Pose(Vector3 p, Vector3 l) { Pos = p; Look = l; } }

		class RecCanvas
		{
			public Canvas Canvas; public RenderMode Mode; public Camera Cam; public float Plane;
			public CanvasScaler Scaler; public CanvasScaler.ScaleMode ScaleMode; public float Factor;
		}

		/// <summary>Seconds of video recorded so far.</summary>
		static float RecSeconds { get { return recFrame / (float)recFps; } }

		[ConsoleCommand(name: "CIRecord", docs: "Dev, anywhere: records the game as numbered 1920x1080 JPGs (video frames) with the game clock fixed at 1/fps per frame. CIRecord start <dir> [fps=30] [cam|camui|screen] [ss 1|2] [sub 1-8] (ss 2: drawn at 3840x2160 and halved; sub: frames averaged per video frame; camui: the camera into a texture plus every overlay canvas; screen: Raft's screen) | stop | pause | resume | status | test <dir> (one frame of each kind)")]
		public static void RecordCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
			if (a == "start")
			{
				if (args.Length < 2) { Fail("CIRecord start <dir> [fps] [cam|camui|screen]"); return; }
				int fps = 30;
				if (args.Length > 2) int.TryParse(args[2], out fps);
				int ss = 1, sub = 1;
				if (args.Length > 4) int.TryParse(args[4], out ss);
				if (args.Length > 5) int.TryParse(args[5], out sub);
				RecStart(args[1], fps, args.Length > 3 ? args[3].ToLowerInvariant() : "camui", ss, sub);
				Log("PASS: recording to " + recDir);
			}
			else if (a == "stop") StartTest(RecStopRoutine());
			else if (a == "pause") { recPaused = true; Log("recording paused at frame " + recFrame); }
			else if (a == "resume") { recPaused = false; Log("recording resumed at frame " + recFrame); }
			else if (a == "test" && args.Length > 1) StartTest(RecTestRoutine(args[1]));
			else Log("RECORD " + (recOn ? "on" : "off") + (recPaused ? " (paused)" : "") + ": frame " + recFrame + ", " + recDir + ", screen " + Screen.width + "x" + Screen.height + ", minimised-safe camera texture " + RecW + "x" + RecH);
		}

		static void RecStart(string dir, int fps, string mode, int ss = 1, int sub = 1)
		{
			if (recOn) RecStopNow();
			recDir = Path.GetFullPath(dir);
			Directory.CreateDirectory(recDir);
			recFps = Mathf.Clamp(fps, 10, 60);
			recScreen = mode == "screen";
			recUi = mode == "camui" || recScreen;
			recFrame = 0;
			recPaused = false;
			recPose = null;
			recOn = true;
			recGfx = GfxShotBegin(); // (CIGfx low: the user's quality for the video; the window stays as it is)
			recSs = recScreen ? 1 : Mathf.Clamp(ss, 1, 2);
			recSub = Mathf.Clamp(sub, 1, 8);
			recSubIdx = 0;
			RecHqBegin(recSs > 1 || recSub > 1);
			Time.captureFramerate = recFps * recSub;
			if (recRt == null) { recRt = new RenderTexture(RecW, RecH, 24); recRt.Create(); }
			if (recSs > 1 && (recBig == null || recBig.width != RecW * recSs))
			{
				if (recBig != null) { recBig.Release(); UnityEngine.Object.Destroy(recBig); }
				recBig = new RenderTexture(RecW * recSs, RecH * recSs, 24) { filterMode = FilterMode.Bilinear };
				recBig.Create();
			}
			if (recTex == null) recTex = new Texture2D(RecW, RecH, TextureFormat.RGB24, false);
			DynamicIslands.instance.StartCoroutine(RecLoop());
			Log("Recording " + recDir + " at " + recFps + " fps, " + mode + (recHq ? ", supersampled x" + recSs + ", " + recSub + " sub-frames, lodBias " + QualitySettings.lodBias.ToString("F1") + ", AA forced" : "") + " (screen " + Screen.width + "x" + Screen.height + ")");
		}

		static IEnumerator RecStopRoutine()
		{
			int frames = recFrame;
			RecStopNow();
			for (float end = Time.realtimeSinceStartup + 30f; Interlocked.CompareExchange(ref recPending, 0, 0) > 0 && Time.realtimeSinceStartup < end; ) yield return null;
			Log("RECORDED " + frames + " frames (" + (frames / (float)recFps).ToString("F1") + " s) in " + recDir);
			Log("PASS: recording stopped");
		}

		static void RecStopNow()
		{
			if (!recOn) return;
			recOn = false;
			recPose = null;
			Time.captureFramerate = 0;
			RecUiRestore();
			RecHqEnd();
			if (recBig != null) { recBig.Release(); UnityEngine.Object.Destroy(recBig); recBig = null; }
			recAcc = null;
			GfxShotEnd(recGfx);
			recGfx = false;
		}

		static IEnumerator RecLoop()
		{
			var eof = new WaitForEndOfFrame();
			while (recOn)
			{
				if (recUi && !recScreen) RecUiConvert();
				yield return eof;
				if (!recOn) break;
				// (an outside "CIGfx low" mid-recording - the keep-alive scripts - drops the quality again: back up for the video)
				if (gfxLow && gfxHigh != null && gfxShotDepth == 0) recGfx = GfxShotBegin();
				if (recPaused) continue;
				RecHqApply();
				bool done;
				try { done = RecCapture(recScreen); }
				catch (Exception e) { Fail("recording frame " + recFrame + ": " + e.Message); RecStopNow(); yield break; }
				if (!done) continue;
				recFrame++; if (recFrame % 30 == 0) hbProg++;
			}
		}

		/// <summary>One frame into recTex and onto disk (encoded here, written on a pool thread). With sub-frames: false
		/// until the last of the video frame's sub-frames, which writes their average.</summary>
		static bool RecCapture(bool screen)
		{
			RenderTexture active = RenderTexture.active;
			if (screen)
			{
				Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
				Graphics.Blit(shot, recRt);
				UnityEngine.Object.Destroy(shot);
			}
			else RecRenderCamera();
			RenderTexture.active = recRt;
			recTex.ReadPixels(new Rect(0, 0, RecW, RecH), 0, 0);
			RenderTexture.active = active;
			if (recSub > 1)
			{
				byte[] raw = recTex.GetRawTextureData();
				if (recAcc == null || recAcc.Length != raw.Length) recAcc = new int[raw.Length];
				int[] acc = recAcc;
				if (recSubIdx == 0) for (int i = 0; i < raw.Length; i++) acc[i] = raw[i];
				else for (int i = 0; i < raw.Length; i++) acc[i] += raw[i];
				if (++recSubIdx < recSub) return false;
				recSubIdx = 0;
				int n = recSub, half = recSub / 2;
				for (int i = 0; i < raw.Length; i++) raw[i] = (byte)((acc[i] + half) / n);
				recTex.LoadRawTextureData(raw);
			}
			recTex.Apply();
			byte[] jpg = recTex.EncodeToJPG(recHq ? 97 : 93);
			string file = Path.Combine(recDir, "f" + recFrame.ToString("D5") + ".jpg");
			Interlocked.Increment(ref recPending);
			ThreadPool.QueueUserWorkItem(_ => { try { File.WriteAllBytes(file, jpg); } catch { } finally { Interlocked.Decrement(ref recPending); } });
			return true;
		}

		/// <summary>The recording's quality on (on = supersampled or sub-frames): the values to give back are taken here.</summary>
		static void RecHqBegin(bool on)
		{
			RecHqEnd();
			if (!on) return;
			recHq = true;
			recHqLod = QualitySettings.lodBias;
			recHqAniso = QualitySettings.anisotropicFiltering;
			recHqAa.Clear();
			try
			{
				GraphicsSettingsBox box = GfxBox();
				if (box != null)
					foreach (var prof in new[] { box.postEffects, box.postEffectsMenu })
						if (prof != null && prof.antialiasing != null && !recHqAa.Any(kv => kv.Key == prof.antialiasing))
							recHqAa.Add(new KeyValuePair<UnityEngine.PostProcessing.AntialiasingModel, KeyValuePair<bool, UnityEngine.PostProcessing.AntialiasingModel.Settings>>(prof.antialiasing, new KeyValuePair<bool, UnityEngine.PostProcessing.AntialiasingModel.Settings>(prof.antialiasing.enabled, prof.antialiasing.settings)));
			}
			catch (Exception e) { Log("(recording: anti-aliasing not read: " + e.Message + ")"); }
			RecHqApply();
		}

		/// <summary>Every frame (a "CIGfx low" or Raft's settings may put theirs back): lodBias x3, anisotropic on, FXAA on.
		/// (FXAA, not TAA: the posed render and the game's own render alternate on one camera - TAA would mix them.)</summary>
		static void RecHqApply()
		{
			if (!recHq) return;
			QualitySettings.lodBias = recHqLod * 3f;
			QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
			foreach (var kv in recHqAa)
			{
				var aa = kv.Key;
				if (!aa.enabled) aa.enabled = true;
				var st = aa.settings;
				if (st.method != UnityEngine.PostProcessing.AntialiasingModel.Method.Fxaa || st.fxaaSettings.preset != UnityEngine.PostProcessing.AntialiasingModel.FxaaPreset.ExtremeQuality)
				{
					st.method = UnityEngine.PostProcessing.AntialiasingModel.Method.Fxaa;
					st.fxaaSettings.preset = UnityEngine.PostProcessing.AntialiasingModel.FxaaPreset.ExtremeQuality;
					aa.settings = st;
				}
			}
		}

		static void RecHqEnd()
		{
			if (!recHq) return;
			recHq = false;
			QualitySettings.lodBias = recHqLod;
			QualitySettings.anisotropicFiltering = recHqAniso;
			foreach (var kv in recHqAa) { kv.Key.settings = kv.Value.Value; kv.Key.enabled = kv.Value.Key; }
			recHqAa.Clear();
		}

		/// <summary>The main camera (at the shot's pose, without the hands and screen drops) into the texture, then the canvases.</summary>
		static void RecRenderCamera()
		{
			Camera cam = Camera.main;
			if (cam == null) cam = UnityEngine.Object.FindObjectsOfType<Camera>().Where(c => c.enabled && c.targetTexture == null).OrderByDescending(c => c.depth).FirstOrDefault();
			if (cam == null) { Graphics.Blit(Texture2D.blackTexture, recRt); return; }
			// (a sub-frame: its own moment between this video frame and the next)
			Pose? pose = recPose != null ? recPose((recFrame + recSubIdx / (float)recSub) / recFps - recPoseStart) : null;
			Vector3 lp = cam.transform.localPosition; Quaternion lr = cam.transform.localRotation;
			int mask = cam.cullingMask;
			RenderTexture before = cam.targetTexture;
			List<Behaviour> drops = null;
			float far = cam.farClipPlane;
			bool fog = RenderSettings.fog;
			UltimateWater.WaterCamera wc = null; object wcFx = null;
			try
			{
				if (pose.HasValue)
				{
					foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.layer != 0) cam.cullingMask &= ~(1 << r.gameObject.layer);
					// (as CIIslandPicture: Raft's haze hides an island a few hundred metres off - fog and the hazy camera effects
					// off for the shot; the volumetric light threw on a texture target)
					drops = ScreenDrops(cam);
					drops.AddRange(cam.GetComponents<Behaviour>().Where(b => b != null && b.enabled && System.Text.RegularExpressions.Regex.IsMatch(b.GetType().Name, "Fog|Scatter|Atmos|Haze|Mist|Volumetric", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
					foreach (Behaviour b in drops) b.enabled = false;
					RenderSettings.fog = false;
					// (Raft's WaterCamera turns the sky's fog on again in OnPreCull while it has image effects - as KeepFogOff)
					wc = cam.GetComponent<UltimateWater.WaterCamera>();
					if (wc != null && recWaterFx != null) { wcFx = recWaterFx.GetValue(wc); recWaterFx.SetValue(wc, null); }
					cam.farClipPlane = Mathf.Max(far, 3000f);
					cam.transform.position = pose.Value.Pos;
					cam.transform.LookAt(pose.Value.Look);
				}
				cam.targetTexture = recSs > 1 && recBig != null ? recBig : recRt;
				cam.aspect = RecW / (float)RecH;
				cam.Render();
				if (cam.targetTexture == recBig) Graphics.Blit(recBig, recRt);
			}
			finally
			{
				cam.targetTexture = before;
				cam.ResetAspect();
				cam.cullingMask = mask;
				cam.farClipPlane = far;
				RenderSettings.fog = fog;
				if (wc != null && wcFx != null) recWaterFx.SetValue(wc, wcFx);
				if (pose.HasValue) { cam.transform.localPosition = lp; cam.transform.localRotation = lr; }
				if (drops != null) foreach (Behaviour b in drops) b.enabled = true;
			}
			if (recUi && recUiCam != null) recUiCam.Render();
		}

		/// <summary>Every overlay canvas onto a camera that draws only them into the frame (an overlay canvas never reaches a
		/// texture), scaled as a 1920x1080 screen would scale it.</summary>
		static void RecUiConvert()
		{
			if (recUiCam == null)
			{
				var go = new GameObject("CIRecordUICam");
				UnityEngine.Object.DontDestroyOnLoad(go);
				go.transform.position = new Vector3(0f, 9000f, 0f);
				recUiCam = go.AddComponent<Camera>();
				recUiCam.enabled = false;
				recUiCam.clearFlags = CameraClearFlags.Depth;
				recUiCam.orthographic = true;
				recUiCam.orthographicSize = 5f;
				recUiCam.nearClipPlane = 0.1f; recUiCam.farClipPlane = 100f;
				recUiCam.cullingMask = 0;
				recUiCam.targetTexture = recRt;
				recUiCam.allowHDR = false; recUiCam.allowMSAA = false;
			}
			recCanvases.RemoveAll(c => c.Canvas == null);
			foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
			{
				if (c == null || !c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
				var rc = new RecCanvas { Canvas = c, Mode = c.renderMode, Cam = c.worldCamera, Plane = c.planeDistance, Scaler = c.GetComponent<CanvasScaler>() };
				if (rc.Scaler != null) { rc.ScaleMode = rc.Scaler.uiScaleMode; rc.Factor = rc.Scaler.scaleFactor; }
				recCanvases.Add(rc);
				recUiCam.cullingMask |= 1 << c.gameObject.layer;
				if (rc.Scaler != null && rc.ScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
				{
					float f = UIKit.ScaleFor(rc.Scaler, new Vector2(RecW, RecH));
					rc.Scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
					rc.Scaler.scaleFactor = f;
				}
				c.renderMode = RenderMode.ScreenSpaceCamera;
				c.worldCamera = recUiCam;
				c.planeDistance = 50f - Mathf.Clamp(c.sortingOrder, -4000, 4000) * 0.01f; // (sorting order kept: higher = nearer)
			}
		}

		static void RecUiRestore()
		{
			foreach (RecCanvas rc in recCanvases)
			{
				if (rc.Canvas == null) continue;
				rc.Canvas.renderMode = rc.Mode;
				rc.Canvas.worldCamera = rc.Cam;
				rc.Canvas.planeDistance = rc.Plane;
				if (rc.Scaler != null) { rc.Scaler.uiScaleMode = rc.ScaleMode; rc.Scaler.scaleFactor = rc.Factor; }
			}
			recCanvases.Clear();
			if (recUiCam != null) recUiCam.cullingMask = 0;
		}

		/// <summary>One frame as Raft's screen gives it and one through the camera texture (with canvases): which one a
		/// minimised Raft gets right.</summary>
		static IEnumerator RecTestRoutine(string dir)
		{
			Directory.CreateDirectory(dir);
			string[] modes = { "screen", "camui" };
			foreach (string m in modes)
			{
				RecStart(Path.Combine(dir, m), 30, m);
				for (int i = 0; i < 6 && recOn; i++) yield return null;
				yield return RecStopRoutine();
			}
			Log("Screen " + Screen.width + "x" + Screen.height + ", full screen " + Screen.fullScreen + ", focused " + Application.isFocused);
			Log("PASS: record test");
		}

		/// <summary>Waits this many seconds of recorded video (frames, not the clock).</summary>
		static IEnumerator RecHold(float seconds)
		{
			int until = recFrame + Mathf.RoundToInt(seconds * recFps);
			while (recOn && recFrame < until) yield return null;
		}

		#endregion

		#region Camera paths

		static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

		static Vector3 CatmullRom(Vector3[] p, float t)
		{
			if (p.Length == 1) return p[0];
			float f = Mathf.Clamp01(t) * (p.Length - 1);
			int i = Mathf.Min((int)f, p.Length - 2);
			float u = f - i;
			Vector3 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Length - 1)];
			return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
		}

		/// <summary>A camera path through these points, looking along these targets, eased in and out over the seconds.</summary>
		static Func<float, Pose?> Path3(float seconds, Vector3[] pos, Vector3[] look)
		{
			return t => { float e = Ease(t / seconds); return new Pose(CatmullRom(pos, e), CatmullRom(look, e)); };
		}

		/// <summary>Points on an arc round centre c: radius r, height h, from angle a0 to a1 (degrees), n points.</summary>
		static Vector3[] Arc(Vector3 c, float a0, float a1, float r0, float r1, float h0, float h1, int n = 5)
		{
			var pts = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				float u = i / (float)(n - 1), a = Mathf.Lerp(a0, a1, u) * Mathf.Deg2Rad, r = Mathf.Lerp(r0, r1, u);
				pts[i] = new Vector3(c.x + Mathf.Cos(a) * r, Mathf.Lerp(h0, h1, u), c.z + Mathf.Sin(a) * r);
			}
			return pts;
		}

		static void RecPath(Func<float, Pose?> path) { recPose = path; recPoseStart = RecSeconds; }

		#endregion

		#region Showcase parts (CIShowcase)

		[ConsoleCommand(name: "CIShowcase", docs: "Dev: the showcase video's parts, each recorded to its own folder. CIShowcase editor <dir> [seed] (editor: Generate island and the island made) | menu <dir> <plan...> (main menu: New Game box, World settings scrolled, then the world is created) | island <dir> <seconds> <orbit|crane|fly|rise> <island...> (world, host: the island brought beside the raft and filmed, then taken away) | hour <h>")]
		public static void ShowcaseCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (a == "editor" && args.Length > 1) { int seed = 4242; if (args.Length > 2) int.TryParse(args[2], out seed); StartTest(ShowcaseEditor(args[1], seed)); }
			else if (a == "menu" && args.Length > 1) StartTest(ShowcaseMenu(args[1], args.Length > 2 ? string.Join(" ", args.Skip(2).ToArray()) : null));
			else if (a == "island" && args.Length > 4)
			{
				float secs; float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs);
				StartTest(ShowcaseIsland(args[1], Mathf.Clamp(secs, 2f, 60f), args[3].ToLowerInvariant(), string.Join(" ", args.Skip(4).ToArray())));
			}
			else if (a == "hour" && args.Length > 1) { float h; float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out h); AzureSkyHour(h); Log("PASS: hour " + h); }
			else if (a == "library" && args.Length > 1) StartTest(ShowcaseLibrary(args[1]));
			else if (a == "quality")
			{
				if (args.Length > 1) int.TryParse(args[1], out scSs);
				if (args.Length > 2) int.TryParse(args[2], out scSub);
				scSs = Mathf.Clamp(scSs, 1, 2); scSub = Mathf.Clamp(scSub, 1, 8);
				Log("PASS: showcase island shots supersampled x" + scSs + ", " + scSub + " sub-frames");
			}
			else if (a == "giants" && args.Length > 3)
			{
				int n; int.TryParse(args[2], out n);
				float secs; float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs);
				StartTest(ShowcaseGiants(args[1], Mathf.Clamp(n, 1, 8), Mathf.Clamp(secs, 2f, 60f), args.Length > 4 ? string.Join(" ", args.Skip(4).ToArray()) : ""));
			}
			else Fail("CIShowcase editor <dir> [seed] | menu <dir> [plan] | island <dir> <seconds> <style> <island> | library <dir> | giants <dir> <n> <seconds> [skip,islands] | quality <ss 1|2> <sub 1-8> | hour <h>");
		}

		/// <summary>Main menu: the island library opened, its Islands tab, then World plans (the storylines), then Islands again,
		/// a row picked on each - about 10.5 s. The pictures are fetched first (unrecorded) so none pops in on the video.</summary>
		static IEnumerator ShowcaseLibrary(string dir)
		{
			if (LoadSceneManager.IsGameSceneLoaded) { Fail("showcase library: from the main menu only"); yield break; }
			hbWhat = "showcase library";
			// (the list is read online: a failed TLS handshake (vid6 11-19, "Unable to complete SSL connection") is tried again)
			for (int attempt = 1; attempt <= 5; attempt++)
			{
				LibraryWindow.Open(1);
				float end = Time.realtimeSinceStartup + 30f;
				while ((LibraryClient.Entries == null || LibraryWindow.Shown().Count == 0) && Time.realtimeSinceStartup < end && LibraryClient.LastError == null) yield return new WaitForSecondsRealtime(0.25f);
				while ((LibraryClient.Entries == null || LibraryWindow.Shown().Count == 0) && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.25f);
				if (LibraryWindow.Shown().Count > 0) break;
				Log("Library: attempt " + attempt + " did not load (" + LibraryWindow.Status + ")");
				hbProg++;
				LibraryWindow.Close();
				yield return new WaitForSecondsRealtime(5f);
			}
			if (LibraryWindow.Shown().Count == 0) { Fail("showcase library: the library's list did not load (" + LibraryWindow.Status + ")"); LibraryWindow.Close(); hbWhat = "idle"; yield break; }
			// the rows to pick: the 3rd island and the 2nd plan (a storyline), when there are that many
			List<string> islands = LibraryWindow.Shown();
			string islandPick = islands[Mathf.Min(2, islands.Count - 1)];
			LibraryWindow.ShowTab(0);
			yield return new WaitForSecondsRealtime(0.5f);
			List<string> plans = LibraryWindow.Shown();
			string planPick = plans.Count > 0 ? plans[Mathf.Min(1, plans.Count - 1)] : null;
			// warm the picture cache (vid7 11-19: the icons and pictures still loaded, white, while it was filmed): the icons of the
			// first rows of both tabs and every picture of both picks, each tried up to 3 times; then they come from memory
			var want = new List<KeyValuePair<LibraryEntry, string>>();
			foreach (string id in islands.Take(11).Concat(plans.Take(11)))
			{
				LibraryEntry e = LibraryClient.Entries.FirstOrDefault(x => x.Info.id == id);
				if (e == null) continue;
				if (!string.IsNullOrEmpty(e.Info.icon)) want.Add(new KeyValuePair<LibraryEntry, string>(e, e.Info.icon));
				if (id == islandPick || id == planPick)
					foreach (string pic in e.Info.pictures) want.Add(new KeyValuePair<LibraryEntry, string>(e, pic));
			}
			int got = 0, missing = 0;
			float warmEnd = Time.realtimeSinceStartup + 300f;
			foreach (var w in want)
			{
				Texture2D tex = null;
				for (int t = 0; t < 3 && tex == null && Time.realtimeSinceStartup < warmEnd; t++)
				{
					bool done = false;
					DynamicIslands.instance.StartCoroutine(LibraryClient.Picture(w.Key, w.Value, x => { tex = x; done = true; }));
					float pe = Time.realtimeSinceStartup + 40f;
					while (!done && Time.realtimeSinceStartup < pe) yield return null;
					hbProg++;
				}
				if (tex != null) got++; else { missing++; Log("Library: picture '" + w.Value + "' of " + w.Key.Info.id + " did not load"); }
			}
			Log("Library: " + got + " pictures ready, " + missing + " missing");
			// start on the first island row, so picking islandPick on the video changes the detail
			LibraryWindow.ShowTab(1);
			yield return new WaitForSecondsRealtime(0.5f);
			{ Button r = LibraryWindow.Row(islands[0]); if (r != null) r.onClick.Invoke(); }
			yield return new WaitForSecondsRealtime(2f);
			Log("Library: " + islands.Count + " islands, " + plans.Count + " plans; picks '" + islandPick + "', '" + planPick + "'");

			RecStart(dir, 30, "camui");
			yield return RecHold(2.0f);
			{ Button r = LibraryWindow.Row(islandPick); if (r != null) r.onClick.Invoke(); }
			yield return RecHold(2.0f);
			LibraryWindow.ShowTab(0);
			yield return RecHold(1.3f);
			if (planPick != null) { Button r = LibraryWindow.Row(planPick); if (r != null) r.onClick.Invoke(); }
			yield return RecHold(2.7f);
			LibraryWindow.ShowTab(1);
			yield return RecHold(2.5f);
			yield return RecStopRoutine();
			LibraryWindow.Close();
			hbWhat = "idle";
			Log("PASS: showcase library");
		}

		/// <summary>World: the biggest island of each storyline (world plan) by land radius, the n biggest of different plans
		/// filmed one by one (a sweeping orbit that shows the whole island) into dir_1, dir_2... skip: islands (comma list) left out.</summary>
		static IEnumerator ShowcaseGiants(string dir, int n, float seconds, string skip)
		{
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("showcase giants: not in a world"); yield break; }
			// (the scan reads every storyline island's file: progress for the heartbeat, or ci.ps1 ends the step as idle - vid6 11-19)
			hbWhat = "showcase giants";
			var skipSet = new HashSet<string>(skip.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);
			var best = new Dictionary<string, KeyValuePair<string, float>>(StringComparer.OrdinalIgnoreCase);
			foreach (string p in WorldPlan.All())
			{
				if (WorldPlan.IsBuiltIn(p) || p.StartsWith("CI ", StringComparison.OrdinalIgnoreCase)) continue;
				string story = p.Replace(" (Custom Islands)", "").Trim();
				WorldPlan plan;
				try { plan = WorldPlan.Load(p); } catch { continue; }
				if (plan == null) continue;
				foreach (IntroRule r in plan.Rules)
				{
					var names = new List<string>();
					if (r.What == "island") names.Add(r.WhatArg.Trim());
					else if (r.What == "oneof") names.AddRange(r.WhatArg.Split(',').Select(x => x.Trim()));
					foreach (string isl in names)
					{
						if (isl.Length == 0 || skipSet.Contains(isl) || !File.Exists(IslandSpawner.PathFor(isl))) continue;
						float land = CustomIslandSpawner.LandRadius(isl);
						hbProg++;
						KeyValuePair<string, float> had;
						if (!best.TryGetValue(story, out had) || land > had.Value) best[story] = new KeyValuePair<string, float>(isl, land);
					}
				}
				Log("Giants: read plan '" + p + "'");
				yield return null;
			}
			var ranked = best.OrderByDescending(kv => kv.Value.Value).ToList();
			foreach (var kv in ranked.Take(12)) Log("Giant: '" + kv.Value.Key + "' of " + kv.Key + ", land radius " + kv.Value.Value.ToString("F0") + " m");
			int i = 0;
			foreach (var kv in ranked.Take(n))
			{
				i++;
				yield return ShowcaseIsland(dir + "_" + i, seconds, "sweep", kv.Value.Key);
			}
			hbWhat = "idle";
			Log("PASS: showcase giants (" + i + ")");
		}

		static IEnumerator ShowcaseEditor(string dir, int seed)
		{
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("showcase editor: no editor"); yield break; }
			DynamicIslands.NewIsland();
			if (GeneratorWindow.IsOpen) GeneratorWindow.Close();
			yield return new WaitForSecondsRealtime(1.5f);
			Terrain terrain = Terrain.activeTerrain ?? UnityEngine.Object.FindObjectOfType<Terrain>();
			Vector3 c = terrain != null ? terrain.transform.position + new Vector3(terrain.terrainData.size.x / 2f, 0f, terrain.terrainData.size.z / 2f) : Vector3.zero;
			float sea = DynamicIslands.EditorWaterLevel;
			Vector3 mid = new Vector3(c.x, sea, c.z);
			// the generator's settings: a big tropical island with hills, buildings, a cave and Raft's features
			GeneratorWindow.Open();
			yield return null;
			GeneratorWindow.ShowTab(GeneratorWindow.TabNormal);
			yield return null;
			IslandGenSettings s = GeneratorWindow.Current ?? IslandGenSettings.RaftLike(seed);
			s.Style = TerrainPainter.Tropical;
			s.Radius = 75f; s.Height = 38f; s.Peaks = 3; s.Roughness = 0.5f; s.ObjectDensity = 0.65f;
			s.Buildings = true; s.BuildingCount = 3; s.Caves = true; s.Features = 4; s.QuestSteps = 0;
			s.Remake = ""; s.Look = "";
			s.Seed = seed;
			GeneratorWindow.Use(s);
			yield return new WaitForSecondsRealtime(1.5f);

			RecStart(dir, 30, "camui");
			float d = 210f;
			Vector3 camFrom = mid + new Vector3(-d * 0.6f, 85f, -d * 0.8f);
			RecPath(t => new Pose(camFrom, mid + Vector3.up * 5f));
			yield return RecHold(1.2f);
			// the seed rolled a few times: the preview map changes
			int[] seeds = { seed + 11, seed + 23, seed };
			foreach (int sd in seeds)
			{
				IslandGenSettings s2 = GeneratorWindow.Current ?? s; s2.Seed = sd; GeneratorWindow.Use(s2);
				yield return RecHold(0.9f);
			}
			yield return RecHold(0.6f);
			// Generate: Raft's scenes load first (real time) - not recorded
			recPaused = true;
			MethodInfo gen = typeof(GeneratorWindow).GetMethod("OnGenerate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			try { gen.Invoke(GeneratorWindow.Instance, null); }
			catch (Exception e) { Fail("showcase editor: Generate threw " + (e.InnerException ?? e).Message); RecStopNow(); yield break; }
			for (float end = Time.realtimeSinceStartup + 240f; GeneratorWindow.IsOpen && Time.realtimeSinceStartup < end; ) yield return new WaitForSecondsRealtime(0.25f);
			if (GeneratorWindow.IsOpen) { Fail("showcase editor: Generate not done after 240 s"); RecStopNow(); yield break; }
			yield return new WaitForSecondsRealtime(3f);
			Log("Generated: " + GeneratorWindow.LastResult);
			// the island made: a slow crane round it, editor panels in view - round the land as generated (its terrain:
			// the land's middle and top from the heights, 11-18: the new island's empty terrain put the camera off the land)
			float top = sea + s.Height;
			Terrain made = Terrain.activeTerrain ?? UnityEngine.Object.FindObjectOfType<Terrain>();
			if (made != null)
			{
				TerrainData td = made.terrainData; Vector3 tp = made.transform.position;
				Vector3 sum = Vector3.zero; int n = 0; float hi = sea;
				for (int i = 0; i < 64; i++) for (int j = 0; j < 64; j++)
				{
					float x = tp.x + td.size.x * (i + 0.5f) / 64f, z = tp.z + td.size.z * (j + 0.5f) / 64f;
					float hy = made.SampleHeight(new Vector3(x, 0f, z)) + tp.y;
					if (hy > sea + 1f) { sum += new Vector3(x, 0f, z); n++; hi = Mathf.Max(hi, hy); }
				}
				if (n > 0) { mid = new Vector3(sum.x / n, sea, sum.z / n); top = hi; }
				Log("Editor island: middle " + mid.ToString("F0") + ", top " + top.ToString("F0") + " (" + n + " land samples), camera was " + camFrom.ToString("F0"));
			}
			Vector3 look = mid + Vector3.up * (s.Height * 0.25f);
			float a0 = Mathf.Atan2(camFrom.z - mid.z, camFrom.x - mid.x) * Mathf.Rad2Deg;
			RecPath(Path3(12.5f, Arc(mid, a0, a0 + 95f, 205f, 150f, top + 70f, top + 22f, 6), new[] { look, look }));
			recPaused = false;
			yield return RecHold(12.5f);
			yield return RecStopRoutine();
			Log("PASS: showcase editor");
		}

		static IEnumerator ShowcaseMenu(string dir, string plan)
		{
			// (the world load says idle on the heartbeat: ci.ps1 ended this step at 46 s, before the world - vid9 11-19)
			hbWhat = "showcase menu";
			float end = Time.realtimeSinceStartup + 30f;
			NewGameBox box;
			while ((box = MenuNewGameBox()) == null && Time.realtimeSinceStartup < end) yield return new WaitForSecondsRealtime(0.1f);
			if (box == null) { hbWhat = "idle"; Fail("showcase menu: no New Game box (main menu?)"); yield break; }
			if (!string.IsNullOrEmpty(plan)) WorldDirector.PendingPlan = plan;
			box.gameObject.SetActive(true);
			try { box.Close(); } catch { } box.Open();
			yield return new WaitForSecondsRealtime(1f);
			// a world name this Raft hasn't used (Create refuses a used one)
			string name = null;
			for (int n = 1; n <= 40 && name == null; n++)
			{
				string tryName = n == 1 ? "Island Adventure" : "Island Adventure " + n;
				box.inputfield_GameName.text = tryName; box.GameNameEndEdit(tryName);
				string solo = Editor.IslandTest.SoloWhenOffline(box);
				if (solo != null && n == 1) Log(solo);
				yield return new WaitForSecondsRealtime(0.3f);
				if (box.createGameButton == null || box.createGameButton.interactable) name = tryName;
			}
			if (name == null) { Fail("showcase menu: no free world name"); yield break; }
			box.inputfield_GameName.text = ""; box.GameNameEndEdit("");
			yield return new WaitForSecondsRealtime(0.5f);

			RecStart(dir, 30, "camui");
			yield return RecHold(0.8f);
			for (int i = 1; i <= name.Length; i++) { box.inputfield_GameName.text = name.Substring(0, i); yield return RecHold(0.06f); }
			box.GameNameEndEdit(name);
			yield return RecHold(0.7f);
			WorldSettingsWindow.Open();
			yield return RecHold(1.0f);
			ScrollRect scroll = WorldSettingsWindow.Window != null ? WorldSettingsWindow.Window.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault(r => r.name == "OptionsScroll") : null;
			if (scroll == null) Log("  (no OptionsScroll in the World settings window)");
			else
			{
				scroll.verticalNormalizedPosition = 1f;
				float secs = 4.5f;
				int frames = Mathf.RoundToInt(secs * recFps);
				for (int f = 0; f <= frames && recOn; f++)
				{
					scroll.velocity = Vector2.zero;
					scroll.verticalNormalizedPosition = 1f - Ease(f / (float)frames);
					yield return RecHold(1f / recFps);
				}
			}
			yield return RecHold(0.7f);
			WorldSettingsWindow.Close();
			yield return RecHold(0.9f);
			yield return RecStopRoutine();
			// the world itself (loading is not recorded)
			yield return NewWorldCreate(box, name);
			AzureSkyHour(10.5f);
			hbWhat = "idle";
			Log("PASS: showcase menu (world '" + name + "')");
		}

		static IEnumerator ShowcaseIsland(string dir, float seconds, string style, string island)
		{
			// (a world still loading from the step before: waited for, up to 3 min)
			for (float wEnd = Time.realtimeSinceStartup + 180f; !LoadSceneManager.IsGameSceneLoaded && Time.realtimeSinceStartup < wEnd; ) { hbProg++; yield return new WaitForSecondsRealtime(1f); }
			if (!LoadSceneManager.IsGameSceneLoaded) { Fail("showcase island: not in a world"); yield break; }
			// (a supersampled shot with sub-frames takes minutes: not idle on the heartbeat)
			string hbWas = hbWhat;
			hbWhat = "showcase island";
			if (!File.Exists(IslandSpawner.PathFor(island))) { Fail("showcase island: no island file '" + island + "'"); yield break; }
			var made = new List<IslandWorldState.Entry>();
			Vector3? spot = ScSpot(island, 420f);
			if (!spot.HasValue) { Fail("showcase island: no open sea for '" + island + "'"); yield break; }
			yield return ScBring(island, spot.Value, made);
			IslandWorldState.Entry e = made.FirstOrDefault();
			if (e == null || e.Root == null) { Fail("showcase island: '" + island + "' did not spawn"); ScRemove(made); yield break; }
			yield return WaitIslandSettled(e, 2f, 12f);
			yield return new WaitForSecondsRealtime(4f);
			// the island's size from what it draws (nothing huge: sea planes, skyboxes)
			Bounds b = new Bounds(e.Root.transform.position, Vector3.one);
			bool any = false;
			foreach (Renderer r in e.Root.GetComponentsInChildren<Renderer>())
			{
				if (r == null || !r.enabled || r.bounds.size.magnitude > 1500f || r is ParticleSystemRenderer) continue;
				if (r.bounds.max.y < -2f) continue; // (under the sea)
				if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
			}
			float land = Mathf.Max(CustomIslandSpawner.LandRadius(island), 20f);
			Vector3 c = new Vector3(b.center.x, 0f, b.center.z);
			float R = Mathf.Clamp(Mathf.Min(Mathf.Max(b.extents.x, b.extents.z), land * 1.4f), 25f, 200f);
			float top = Mathf.Clamp(b.max.y, 4f, 160f);
			Log("SHOT '" + island + "' at " + c.ToString("F0") + ": radius " + R.ToString("F0") + " m (land " + land.ToString("F0") + "), top " + top.ToString("F0") + " m, " + style + " " + seconds + " s");
			// the camera on the side away from the raft's line: the raft out of the picture
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			Vector3 toRaft = raft.HasValue ? Flat(raft.Value - c).normalized : Vector3.forward;
			float aRaft = Mathf.Atan2(toRaft.z, toRaft.x) * Mathf.Rad2Deg;
			float side = aRaft + 180f;
			Vector3 look = c + Vector3.up * Mathf.Clamp(top * 0.3f, 3f, 30f);
			float d = R * 1.2f + 20f, h = R * 0.35f + top * 0.5f + 8f;
			Vector3[] pos;
			Vector3[] looks = { look, look };
			switch (style)
			{
				case "crane": pos = Arc(c, side - 25f, side + 25f, d * 1.25f, d * 0.95f, h * 2.0f, h * 0.7f); break;
				case "fly":
					pos = Arc(c, side - 55f, side + 15f, d * 1.2f, d * 0.85f, Mathf.Max(top * 0.5f, 10f) + 6f, Mathf.Max(top * 0.6f, 12f) + 10f);
					looks = new[] { look - toRaft * R * 0.2f, look, look + toRaft * R * 0.1f };
					break;
				case "rise": pos = Arc(c, side + 20f, side - 10f, d * 0.85f, d * 1.25f, 5f, h * 1.6f); break;
				// (a giant island: a long high sweep round it, coming down as it goes - the whole island, then its shore)
				case "sweep": pos = Arc(c, side - 70f, side + 35f, d * 1.25f, d * 0.95f, h * 1.7f, h * 0.75f, 7); break;
				default: pos = Arc(c, side - 40f, side + 40f, d, d, h, h); break;
			}
			AzureSkyHour(10.5f);
			yield return null;
			RecStart(dir, 30, "cam", scSs, scSub);
			RecPath(Path3(seconds, pos, looks));
			yield return RecHold(seconds);
			yield return RecStopRoutine();
			yield return RemoveIslandsAndWait(made.Where(x => x != null).Select(x => x.Id).ToList(), 3f);
			made.Clear();
			hbWhat = hbWas;
			Log("PASS: showcase island " + island);
		}

		#endregion
	}
}
