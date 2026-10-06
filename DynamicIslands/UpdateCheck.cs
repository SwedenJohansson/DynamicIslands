using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The update prompt (ROADMAP AU44): once per Raft start, when the main menu appears, the mod asks GitHub for its latest
	/// release (SwedenJohansson/DynamicIslands, the release's tag) and says when a newer Custom Islands is out. A short
	/// timeout, and any failure is silent (only the log says it). updatecheck=off in Mods\DynamicIslands\world_rules.txt
	/// switches it off, and so does online = off in library.txt. It also words the notice of a host or player with another
	/// version of the mod (AU35): both versions, and which one to install.
	/// </summary>
	public static class UpdateCheck
	{
		public const string ReleasesUrl = "https://api.github.com/repos/SwedenJohansson/DynamicIslands/releases/latest";
		/// <summary>world_rules.txt's key: off = no check.</summary>
		public const string SettingKey = "updatecheck";
		/// <summary>The mod's version when modinfo.json couldn't be read (ExperimentalNotice.Version).</summary>
		public const string Unknown = "?";
		/// <summary>The version of a host or player from before the mod sent its version.</summary>
		public const string Older = "an older version";
		const int TimeoutSeconds = 8;

		static bool started, told;

		/// <summary>The latest release's version (its tag, without a leading "v"), or null: not asked, switched off, or it failed.</summary>
		public static string Latest { get; private set; }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [update] " + msg); }

		/// <summary>Whether the check may go online (updatecheck=off, or the library's online = off, switch it off).</summary>
		public static bool Enabled
		{
			get { return !"off".Equals(WorldRules.ReadDefault(SettingKey) ?? "", StringComparison.OrdinalIgnoreCase) && LibraryClient.Online; }
		}

		/// <summary>A version that can be compared: read from modinfo.json, and told (not "?", not from before versions were sent).</summary>
		public static bool IsKnown(string version)
		{
			return !string.IsNullOrEmpty(version) && version != Unknown && version != Older && char.IsDigit(version[0]);
		}

		/// <summary>The main menu appeared (DynamicIslands.HookUI): the first time, asks GitHub; later, a newer version found
		/// while the player was in a world is told now. Never more than once per start.</summary>
		public static void OnMainMenu()
		{
			if (started) { TellIfNewer(); return; }
			started = true;
			bool on;
			try { on = Enabled; }
			catch (Exception e) { Log("Not checking: " + e.Message); return; }
			if (!on) { Log("Switched off (" + SettingKey + "=off in " + WorldRules.DefaultFileName + ", or online = off in library.txt)"); return; }
			if (DynamicIslands.instance == null) return;
			try { DynamicIslands.instance.StartCoroutine(Fetch()); }
			catch (Exception e) { Log("Not checking: " + e.Message); }
		}

		static IEnumerator Fetch()
		{
			UnityWebRequest req = null;
			try
			{
				req = UnityWebRequest.Get(ReleasesUrl);
				req.timeout = TimeoutSeconds;
				req.SetRequestHeader("Accept", "application/vnd.github+json");
			}
			catch (Exception e) { Log("Not checking: " + e.Message); }
			if (req == null) yield break;
			string text = null;
			using (req)
			{
				yield return req.SendWebRequest();
				if (req.result == UnityWebRequest.Result.Success) text = req.downloadHandler.text;
				else Log("No answer from GitHub (" + req.error + ")");
			}
			if (text == null) yield break;
			try
			{
				var root = LibraryJson.Parse(text.TrimStart('\uFEFF')) as Dictionary<string, object>;
				Latest = root != null ? Clean(LibraryJson.Str(root, "tag_name")) : null;
			}
			catch (Exception e) { Log("GitHub's answer can't be read: " + e.Message); Latest = null; }
			Log("The latest release is " + (Latest ?? "(no version in its tag)") + ", this is " + LibraryPack.ModVersion);
			// (still on the main menu: now; else the next time it shows)
			if (!LoadSceneManager.IsGameSceneLoaded && GameObject.Find("MainMenuCanvas") != null) TellIfNewer();
		}

		/// <summary>A tag as a version: "v3.1" and "3.1" are 3.1 (from its first digit, digits and dots), null if it has none.</summary>
		public static string Clean(string tag)
		{
			string t = (tag ?? "").Trim();
			int i = 0;
			while (i < t.Length && !char.IsDigit(t[i])) i++;
			string v = new string(t.Substring(i).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');
			return v.Length > 0 ? v : null;
		}

		static void TellIfNewer()
		{
			string mine = LibraryPack.ModVersion;
			if (told || !IsKnown(Latest) || !IsKnown(mine) || LibraryPack.CompareVersions(Latest, mine) <= 0) return;
			told = true;
			DynamicIslands.Notify("A newer Custom Islands is out (" + Latest + ", you have " + mine + "): get it from raftmodding.com or GitHub (SwedenJohansson/DynamicIslands). " +
				"(" + SettingKey + "=off in Mods\\DynamicIslands\\" + WorldRules.DefaultFileName + " stops this check)", false, 15);
		}

		/// <summary>
		/// The notice of a version difference in multiplayer (IslandNetwork.CompareVersions): onHost = this machine hosts and
		/// who joined with theirs; else theirs is the host's. Names both versions and which one to install (the newer of
		/// the two; the newest release too, when it is newer still).
		/// </summary>
		public static string MismatchText(bool onHost, string who, string theirs, string mine)
		{
			string head = onHost
				? who + " joined with Custom Islands " + theirs + " - you have " + mine + "."
				: "The host has Custom Islands " + theirs + " - you have " + mine + ".";
			string newer = null;
			if (IsKnown(theirs) && IsKnown(mine)) newer = LibraryPack.CompareVersions(theirs, mine) > 0 ? theirs : mine;
			else if (theirs == Older && IsKnown(mine)) newer = mine;
			string advice;
			if (newer == null)
				advice = " One version can't be told: both should install the newest Custom Islands" + (IsKnown(Latest) ? " (" + Latest + ")" : "") + ".";
			else if (onHost)
				advice = newer == mine
					? " " + who + " should install Custom Islands " + mine + "."
					: " Update to Custom Islands " + theirs + " (or they install " + mine + ").";
			else
				advice = newer == theirs
					? " Install Custom Islands " + theirs + " to match the host."
					: " The host should update to Custom Islands " + mine + " (or you install " + theirs + ").";
			if (newer != null && IsKnown(Latest) && LibraryPack.CompareVersions(Latest, newer) > 0) advice += " The newest is " + Latest + ": best both update to it.";
			return head + advice + " Until then islands, quests and settings may not match, or not arrive at all.";
		}
	}
}
