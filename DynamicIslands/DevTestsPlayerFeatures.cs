using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>Tests of the player features PF1 (own notebook), PF4 (camera fly-throughs) and PF5 (minimap and pings).</summary>
	public static partial class DevTests
	{
		#region PF1 player notebook

		const string NotebookTestTitle = "CI notebook ";

		[ConsoleCommand(name: "CINotebook", docs: "Dev, world 'CI ...' (PF1): the player's own notebook. CINotebook unit - the file format round trip, the window (typing, new page, player held, saved on close, the file next to the world's save), copying a note in / write - adds a page 'CI notebook <world>' and saves / check - after loading the world again: its page is back, no other world's page / empty - a world with no 'CI notebook' page (another world's are not here)")]
		public static void NotebookCommand(string[] args) { StartTest(NotebookRoutine(args != null && args.Length > 0 ? args[0] : "unit")); }

		static IEnumerator NotebookRoutine(string what)
		{
			string world = SaveAndLoad.CurrentGameFileName ?? "";
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor() || !world.StartsWith("CI ")) { Fail("notebook: in a test world 'CI ...'"); yield break; }
			// (the notebook loads a frame after the world's id is known)
			float wait = Time.unscaledTime + 10f;
			while (PlayerNotebook.LoadedFor != SaveAndLoad.WorldGuid.ToString() && Time.unscaledTime < wait) yield return null;
			bool ok = true;
			Check(ref ok, PlayerNotebook.LoadedFor == SaveAndLoad.WorldGuid.ToString(), "the notebook belongs to this world (" + PlayerNotebook.LoadedFor + ")");
			string mine = NotebookTestTitle + world;
			if (what == "write")
			{
				PlayerNotebook.Pages.RemoveAll(p => p.Title == mine);
				PlayerNotebook.AddPage(mine, "Written in " + world + "\n=== not a new page\n\\ a backslash line\n");
				PlayerNotebook.Save();
				string to = PlayerNotebook.PathFor(PlayerNotebook.LoadedFor);
				Check(ref ok, File.Exists(to) && File.ReadAllText(to).Contains(mine), "saved to " + to);
				if (ok) Log("PASS: notebook write (" + world + ")"); else Fail("notebook write");
				yield break;
			}
			if (what == "check" || what == "empty")
			{
				List<PlayerNotebook.Page> ci = PlayerNotebook.Pages.Where(p => p.Title.StartsWith(NotebookTestTitle)).ToList();
				if (what == "empty") Check(ref ok, ci.Count == 0, "no test page in this world (" + string.Join(", ", ci.Select(p => p.Title).ToArray()) + ")");
				else
				{
					PlayerNotebook.Page p = ci.FirstOrDefault(x => x.Title == mine);
					Check(ref ok, p != null && p.Text == "Written in " + world + "\n=== not a new page\n\\ a backslash line\n", "this world's page is back, its text exact: " + (p == null ? "(missing)" : p.Text.Replace("\n", "|")));
					Check(ref ok, ci.All(x => x.Title == mine), "no other world's page: " + string.Join(", ", ci.Select(x => x.Title).ToArray()));
					Check(ref ok, (PlayerNotebook.LastPath ?? "").EndsWith(PlayerNotebook.FileName) && WorldCopy.RaftWorldFolder != null && Path.GetDirectoryName(PlayerNotebook.LastPath) == WorldCopy.RaftWorldFolder, "read from the world's own folder: " + PlayerNotebook.LastPath);
				}
				if (ok) Log("PASS: notebook " + what + " (" + world + ")"); else Fail("notebook " + what);
				yield break;
			}

			// unit
			var sample = new List<PlayerNotebook.Page>
			{
				new PlayerNotebook.Page { Title = "First", Text = "one\n\ntwo\n" },
				new PlayerNotebook.Page { Title = "Tricky", Text = "=== looks like a page\n\\starts with a backslash\n# a hash" },
				new PlayerNotebook.Page { Title = "", Text = "" },
			};
			List<PlayerNotebook.Page> back = PlayerNotebook.Parse(PlayerNotebook.Serialize(sample));
			Check(ref ok, back.Count == 3 && Enumerable.Range(0, 3).All(i => back[i].Title == sample[i].Title && back[i].Text == sample[i].Text), "the file format gives back every page exactly: " + string.Join(" / ", back.Select(p => p.Title + ":" + p.Text.Replace("\n", "|")).ToArray()));
			string other = PlayerNotebook.PathFor(Guid.NewGuid().ToString());
			Check(ref ok, other != PlayerNotebook.PathFor(PlayerNotebook.LoadedFor) && !File.Exists(other), "another world has a file of its own (" + other + ")");

			int before = PlayerNotebook.Pages.Count;
			PlayerNotebookWindow.Open();
			yield return null;
			Check(ref ok, PlayerNotebookWindow.IsOpen && PlayerNotebookWindow.PlayerHeld, "the window opens and holds the player");
			PlayerNotebookWindow.NewPageForTest();
			PlayerNotebookWindow.TypeForTest("CI typed page", "typed line 1\ntyped line 2");
			yield return null;
			Check(ref ok, PlayerNotebook.Pages.Count == before + 1 && PlayerNotebook.Pages.Last().Title == "CI typed page" && PlayerNotebook.Pages.Last().Text == "typed line 1\ntyped line 2", "typing fills a new page");
			Check(ref ok, PlayerNotebookWindow.ListedPages == PlayerNotebook.Pages.Count, "the list shows every page (" + PlayerNotebookWindow.ListedPages + ")");
			Screenshot(new[] { "player_notebook" });
			yield return new WaitForSeconds(0.5f);
			PlayerNotebookWindow.Close();
			yield return null;
			string path = PlayerNotebook.PathFor(PlayerNotebook.LoadedFor);
			Check(ref ok, !PlayerNotebookWindow.IsOpen && File.Exists(path) && File.ReadAllText(path).Contains("=== CI typed page") && File.ReadAllText(path).Contains("typed line 2"), "saved on closing to " + path);
			Check(ref ok, WorldCopy.RaftWorldFolder == null || Path.GetDirectoryName(path) == WorldCopy.RaftWorldFolder, "the file is next to the world's save (" + WorldCopy.RaftWorldFolder + ")");
			Check(ref ok, PlayerNotebook.CopyIn("CI copied note", "copied text") && PlayerNotebook.Pages.Last().Title == "CI copied note" && File.ReadAllText(path).Contains("copied text"), "a note copied in is a new page, saved at once");
			// (journal: the copy button on a page)
			PlayerNotebook.Pages.RemoveAll(p => p.Title == "CI typed page" || p.Title == "CI copied note");
			PlayerNotebook.MarkChanged();
			PlayerNotebook.Save();
			Check(ref ok, !File.ReadAllText(path).Contains("CI typed page"), "the test pages removed again");
			if (ok) Log("PASS: notebook unit"); else Fail("notebook unit");
		}

		#endregion
	}
}
