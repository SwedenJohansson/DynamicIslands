using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>Keys of an island's own settings (IslandFile.Props, format 4), edited on the Island tab.</summary>
	public static class IslandProps
	{
		public const string Title = "info.title", Author = "info.author", Description = "info.description";
		/// <summary>Rules: in-game days until harvested things, killed animals and looted chests come back on this island ("" = the world's regrowDays, 0 = never).</summary>
		public const string RegrowDays = "rules.regrow";
	}

	/// <summary>A spawned island's own settings (IslandFile.Props), for the parts of the mod that act on it in a world.</summary>
	public class IslandSettings : MonoBehaviour
	{
		public Dictionary<string, string> Props = new Dictionary<string, string>();
	}

	/// <summary>The island rules editor's result: per-island overrides of the world's settings.</summary>
	public static class IslandRules
	{
		/// <summary>Days until things come back on this island: its own rule, or the world's regrowDays.</summary>
		public static int RegrowDays(IslandWorldState.Entry e)
		{
			IslandSettings s = e != null && e.Root != null ? e.Root.GetComponent<IslandSettings>() : null;
			int days;
			if (s != null && int.TryParse(ObjectProps.Get(s.Props, IslandProps.RegrowDays), out days)) return Mathf.Max(0, days);
			return CustomIslandSpawner.RegrowDays;
		}
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
		public static void ForgetShown() { shown.Clear(); LastShown = null; LastMessage = null; }

		public static void Tag(GameObject root, IslandFile island)
		{
			root.AddComponent<IslandSettings>().Props = new Dictionary<string, string>(island.Props);
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

		/// <summary>A trigger zone's message, in the same banner (without a title).</summary>
		public static void ShowMessage(string text)
		{
			Show("", "", text);
			LastMessage = text;
		}

		/// <summary>The last zone message shown (the automated tests look at it).</summary>
		public static string LastMessage { get; private set; }

		public static void Show(string title, string author, string description)
		{
			if (banner == null) Build();
			titleText.gameObject.SetActive(title.Length > 0 || description.Length == 0);
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
			UIKit.Surface(panel); // Raft's menu look
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
