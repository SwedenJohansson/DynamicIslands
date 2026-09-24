using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>Tools for the editor's look (Raft's own menus as the model).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIUIDump", docs: "Dev: writes Raft's UI (canvases; images with their sprites, texts with their fonts) to Mods\\DynamicIslands\\ui_dump_<scene>.txt and the sprites to ui_sprites\\. CIUIDump [part of a path]")]
		public static void UIDump(string[] args)
		{
			string filter = args != null && args.Length > 0 ? string.Join(" ", args) : "";
			string dir = Path.Combine(DynamicIslands.assetpath, "ui_sprites");
			Directory.CreateDirectory(dir);
			var sb = new StringBuilder();
			var saved = new HashSet<string>();
			var fonts = new Dictionary<string, int>();
			foreach (Canvas c in Resources.FindObjectsOfTypeAll<Canvas>())
			{
				if (c == null || !c.gameObject.scene.IsValid() || !c.isRootCanvas || c.name.StartsWith("CustomIslands")) continue;
				sb.AppendLine("=== CANVAS " + c.name + " (scene " + c.gameObject.scene.name + ", active " + c.gameObject.activeInHierarchy + ", order " + c.sortingOrder + ")");
				CanvasScaler cs = c.GetComponent<CanvasScaler>();
				if (cs != null) sb.AppendLine("    scaler " + cs.uiScaleMode + " ref " + cs.referenceResolution + " match " + cs.matchWidthOrHeight);
				foreach (Transform t in c.GetComponentsInChildren<Transform>(true))
				{
					string path = PathOf(t, c.transform);
					if (filter.Length > 0 && path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
					var parts = new List<string>();
					RectTransform rt = t as RectTransform;
					if (rt != null) parts.Add("size " + rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0"));
					foreach (Component comp in t.GetComponents<Component>())
					{
						if (comp == null || comp is Transform || comp is CanvasRenderer) continue;
						Image img = comp as Image;
						Text txt = comp as Text;
						Selectable sel = comp as Selectable;
						Shadow sh = comp as Shadow;
						if (img != null)
						{
							Sprite s = img.sprite;
							parts.Add("Image[" + (s != null ? s.name + " tex " + (s.texture != null ? s.texture.name : "?") + " " + s.rect.width + "x" + s.rect.height + " border " + s.border + " ppu " + s.pixelsPerUnit : "no sprite") +
								" " + img.type + " col " + Hex(img.color) + (img.material != null && img.material.name != "Default UI Material" ? " mat " + img.material.name : "") + " ppuMul " + img.pixelsPerUnitMultiplier + "]");
							if (s != null && saved.Add(s.name)) SaveSprite(s, dir);
						}
						else if (txt != null)
						{
							string fn = txt.font != null ? txt.font.name : "?";
							fonts[fn] = (fonts.ContainsKey(fn) ? fonts[fn] : 0) + 1;
							parts.Add("Text[" + fn + " " + txt.fontSize + " " + txt.fontStyle + " " + Hex(txt.color) + " " + txt.alignment + " spacing " + txt.lineSpacing + " best " + txt.resizeTextForBestFit + " '" + Short(txt.text) + "']");
						}
						else if (sel != null)
						{
							ColorBlock cb = sel.colors;
							parts.Add(comp.GetType().Name + "[" + sel.transition + " n " + Hex(cb.normalColor) + " h " + Hex(cb.highlightedColor) + " p " + Hex(cb.pressedColor) + " d " + Hex(cb.disabledColor) +
								(sel.spriteState.highlightedSprite != null ? " hSprite " + sel.spriteState.highlightedSprite.name : "") + (sel.spriteState.pressedSprite != null ? " pSprite " + sel.spriteState.pressedSprite.name : "") + "]");
							foreach (Sprite s in new[] { sel.spriteState.highlightedSprite, sel.spriteState.pressedSprite, sel.spriteState.selectedSprite, sel.spriteState.disabledSprite })
								if (s != null && saved.Add(s.name)) SaveSprite(s, dir);
						}
						else if (sh != null) parts.Add(comp.GetType().Name + "[" + Hex(sh.effectColor) + " " + sh.effectDistance + "]");
						else parts.Add(comp.GetType().Name);
					}
					sb.AppendLine((t.gameObject.activeInHierarchy ? "  " : "- ") + path + "  " + string.Join("  ", parts.ToArray()));
				}
			}
			sb.AppendLine("=== FONTS used: " + string.Join(", ", fonts.Select(f => f.Key + " \u00D7" + f.Value).ToArray()));
			sb.AppendLine("=== FONTS loaded: " + string.Join(", ", Resources.FindObjectsOfTypeAll<Font>().Select(f => f.name).Distinct().ToArray()));
			string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
			string file = Path.Combine(DynamicIslands.assetpath, "ui_dump_" + scene + ".txt");
			File.WriteAllText(file, sb.ToString());
			Log("UI dump: " + file + " (" + saved.Count + " sprites saved to " + dir + ")");
		}

		[ConsoleCommand(name: "CIClock", docs: "Dev, in game: the clocks Raft shares between players (water time) next to Time.time")]
		public static void Clock()
		{
			Network_Water w = ComponentManager<Network_Water>.Value;
			Log("Clock: water " + (w != null ? w.WaterTime.ToString("0.00") : "none") + ", Time.time " + Time.time.ToString("0.00") + ", shared " + SharedClock.Now.ToString("0.00") + (SharedClock.Synced ? "" : " (not synced)") + ", day " + WorldManager.DayCounter);
		}

		static string PathOf(Transform t, Transform top)
		{
			string p = t.name;
			for (Transform x = t.parent; x != null && x != top.parent; x = x.parent) p = x.name + "/" + p;
			return p;
		}

		static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGBA(c); }

		static string Short(string s)
		{
			s = (s ?? "").Replace("\n", " ");
			return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
		}

		/// <summary>Saves a sprite's part of its (maybe unreadable) texture as a PNG, through a render texture.</summary>
		static void SaveSprite(Sprite s, string dir)
		{
			try
			{
				Texture2D src = s.texture;
				if (src == null) return;
				RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				RenderTexture prev = RenderTexture.active;
				Graphics.Blit(src, rt);
				RenderTexture.active = rt;
				Rect r = s.textureRect;
				var tex = new Texture2D(Mathf.Max(1, (int)r.width), Mathf.Max(1, (int)r.height), TextureFormat.RGBA32, false);
				tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
				tex.Apply();
				RenderTexture.active = prev;
				RenderTexture.ReleaseTemporary(rt);
				string name = new string(s.name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
				File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
				UnityEngine.Object.Destroy(tex);
			}
			catch (Exception e) { Debug.LogWarning("[CITEST] sprite " + s.name + ": " + e.Message); }
		}
	}
}
