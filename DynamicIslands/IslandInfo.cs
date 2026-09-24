using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>Keys of an island's own settings (IslandFile.Props, format 4), edited on the Island tab.</summary>
	public static class IslandProps
	{
		public const string Title = "info.title", Author = "info.author", Description = "info.description";
	}

	/// <summary>What a spawned island tells players about itself, and where its land is.</summary>
	public class IslandInfoTag : MonoBehaviour
	{
		public string FileName, Title, Author, Description;
		/// <summary>Centre of the land relative to the island's root, and how far the land reaches from it.</summary>
		public Vector3 LocalCentre;
		public float Radius;
	}

	/// <summary>
	/// The island info editor's result in a world: when a player first comes close to an island that has a name or a
	/// description, a banner shows it at the top of the screen for a few seconds ("Skull Rock - by Johan - Beware of
	/// the bears"). Once per island per session.
	/// </summary>
	public static class IslandInfo
	{
		const float ShowDistance = 30f, ShowSeconds = 8f;
		static readonly HashSet<string> shown = new HashSet<string>();
		static float nextCheck;
		static CanvasGroup banner;
		static Text titleText, authorText, descText;
		static float shownAt = -100f;

		/// <summary>The text of the last banner shown (the automated tests look at it).</summary>
		public static string LastShown { get; private set; }

		/// <summary>Forgets which islands were announced, so their banners show again (tests).</summary>
		public static void ForgetShown() { shown.Clear(); LastShown = null; }

		public static void Tag(GameObject root, IslandFile island)
		{
			string title = ObjectProps.Get(island.Props, IslandProps.Title), desc = ObjectProps.Get(island.Props, IslandProps.Description);
			if (title.Length == 0 && desc.Length == 0) return;
			IslandInfoTag tag = root.AddComponent<IslandInfoTag>();
			tag.FileName = island.Name;
			tag.Title = title;
			tag.Author = ObjectProps.Get(island.Props, IslandProps.Author);
			tag.Description = desc;
			Vector2 land = IslandSpawner.LandCentre(island);
			tag.LocalCentre = new Vector3(land.x, island.WaterLevel, land.y);
			tag.Radius = IslandSpawner.LandRadius(island);
		}

		public static void Tick()
		{
			if (banner != null && banner.gameObject.activeSelf)
			{
				float t = Time.unscaledTime - shownAt;
				banner.alpha = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((ShowSeconds - t) / 1.2f);
				if (t > ShowSeconds) banner.gameObject.SetActive(false);
			}
			if (Time.unscaledTime < nextCheck) return;
			nextCheck = Time.unscaledTime + 0.5f;
			if (!LoadSceneManager.IsGameSceneLoaded) { shown.Clear(); return; }
			Network_Player player = null;
			try { player = ComponentManager<Network_Player>.Value; } catch { }
			if (player == null) return;
			foreach (GameObject root in IslandSpawner.SpawnedRoots)
			{
				if (root == null) continue;
				IslandInfoTag tag = root.GetComponent<IslandInfoTag>();
				if (tag == null || shown.Contains(tag.FileName)) continue;
				Vector3 c = root.transform.position + tag.LocalCentre, p = player.transform.position;
				if (new Vector2(c.x - p.x, c.z - p.z).magnitude > tag.Radius + ShowDistance) continue;
				shown.Add(tag.FileName);
				Show(tag.Title, tag.Author, tag.Description);
			}
		}

		public static void Show(string title, string author, string description)
		{
			if (banner == null) Build();
			titleText.text = title.Length > 0 ? title : "Unknown island";
			authorText.text = author.Length > 0 ? "by " + author : "";
			authorText.gameObject.SetActive(author.Length > 0);
			descText.text = description;
			descText.gameObject.SetActive(description.Length > 0);
			banner.gameObject.SetActive(true);
			banner.alpha = 0f;
			shownAt = Time.unscaledTime;
			LastShown = titleText.text + (author.Length > 0 ? " | " + authorText.text : "") + (description.Length > 0 ? " | " + description : "");
			Debug.Log("[CUSTOM ISLANDS] Arriving at: " + LastShown);
		}

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_IslandBanner", 400);
			Object.DontDestroyOnLoad(canvas.gameObject);
			RectTransform panel = UIKit.Rect("Banner", canvas.transform);
			UIKit.Anchor(panel, new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(620, 0));
			UIKit.Background(panel.gameObject, new Color(0.05f, 0.06f, 0.08f, 0.72f), 10);
			UIKit.Vertical(panel.gameObject, 4f, new RectOffset(24, 24, 14, 16), true);
			titleText = UIKit.Label(panel, "", 30, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
			authorText = UIKit.Label(panel, "", 14, UIKit.TextMuted, TextAnchor.MiddleCenter, FontStyle.Italic, "Author");
			descText = UIKit.Label(panel, "", 17, UIKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Normal, "Description");
			descText.supportRichText = false;
			foreach (Graphic g in panel.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
			banner = canvas.gameObject.AddComponent<CanvasGroup>();
			banner.interactable = false; banner.blocksRaycasts = false;
			canvas.gameObject.SetActive(false);
		}
	}
}
