using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft buildings (TODO 11-24): single structures of Raft's story islands (a Tangaroa house, a Caravan Town shop, a
	/// radio tower section...) as object groups that ship with the mod, in the object browser's "Raft buildings" category
	/// next to "My groups". Placed like a group: one undo step, and the parts stay separate objects to edit.
	///
	/// Files: raft_buildings/buildings.txt (lines "file|name|scene|count") and raft_buildings/&lt;file&gt;.group (GroupLibrary's
	/// format, made from Raft's scenes by the dev command CICaptureBuildings). Each loads (its island's scene) when it is
	/// first picked, so an opened category costs nothing.
	/// </summary>
	public static class RaftBuildings
	{
		public const string Category = "Raft buildings";
		public const string Prefix = GroupLibrary.Prefix + "Raft/";
		public const string Folder = "raft_buildings";

		public class Building
		{
			public string File, Name, Scene;
			public int Count;
			/// <summary>A whole piece of Raft's scene as one object (the Vasagatan's hull: its meshes aren't catalog
			/// objects): its catalog name ("whole:&lt;name&gt;" in buildings.txt) and node path; null for a group.</summary>
			public string Whole, Path;
			public string CatalogName { get { return Whole ?? Prefix + Name; } }
		}

		static List<Building> all;
		/// <summary>The buildings the mod ships, in list order.</summary>
		public static List<Building> All { get { if (all == null) all = ReadList(); return all; } }

		public static bool Is(string catalogName) { return catalogName != null && (catalogName.StartsWith(Prefix) || IsWhole(catalogName)); }

		static HashSet<string> wholeNames;
		/// <summary>Whether this is a whole piece of Raft's scene (one object, saved in islands by this name).</summary>
		public static bool IsWhole(string catalogName)
		{
			if (wholeNames == null) wholeNames = new HashSet<string>(All.Where(b => b.Whole != null).Select(b => b.Whole));
			return catalogName != null && wholeNames.Contains(catalogName);
		}

		public static Building Find(string catalogName) { return Is(catalogName) ? All.FirstOrDefault(b => b.CatalogName == catalogName) : null; }

		/// <summary>The label of a building not loaded yet: its name and part count, as it shows once loaded.</summary>
		public static string Label(string catalogName)
		{
			Building b = Find(catalogName);
			if (b != null && b.Whole != null) return b.Name;
			return b != null ? b.Name + " (" + b.Count + ")" : catalogName.Substring(Math.Min(catalogName.Length, Prefix.Length));
		}

		static List<Building> ReadList()
		{
			var list = new List<Building>();
			byte[] bytes = RaftIslands.ModFile(Folder + "/buildings.txt");
			if (bytes == null) return list;
			foreach (string raw in Encoding.UTF8.GetString(bytes).Split('\n'))
			{
				string line = raw.Trim().TrimStart('﻿');
				if (line.Length == 0 || line.StartsWith("#")) continue;
				string[] f = line.Split('|');
				int n;
				if (f.Length < 4 || !int.TryParse(f[3], out n)) { Debug.LogWarning("[CUSTOM ISLANDS] raft_buildings/buildings.txt: line not understood: " + line); continue; }
				var b = new Building { File = f[0], Name = f[1], Scene = f[2], Count = n };
				if (f[0].StartsWith("whole:")) { b.Whole = f[0].Substring(6); b.File = b.Whole; b.Path = f.Length > 4 ? f[4] : ""; }
				list.Add(b);
			}
			return list;
		}

		/// <summary>Reads one building's group file (null when the mod has none).</summary>
		public static List<GroupLibrary.Member> Members(Building b)
		{
			byte[] bytes = RaftIslands.ModFile(Folder + "/" + b.File + ".group");
			if (bytes == null) { Debug.LogWarning("[CUSTOM ISLANDS] Raft building '" + b.Name + "': " + Folder + "/" + b.File + ".group is missing from the mod"); return null; }
			try { return GroupLibrary.Read(new MemoryStream(bytes), b.Name); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Raft building '" + b.Name + "' can't be read: " + e.Message); return null; }
		}

		/// <summary>Puts the buildings among these catalog names that aren't loaded yet in the object catalog (their islands'
		/// scenes load first).</summary>
		public static IEnumerator EnsureRegistered(IEnumerable<string> names)
		{
			foreach (string name in names.Where(n => Is(n) && !PlaceableCatalog.IsLoaded(n)).Distinct().ToList())
			{
				Building b = Find(name);
				if (b != null && b.Whole != null) { yield return PlaceableCatalog.VisitScene(PlaceableCatalog.ResolveScene(b.Scene), s => TakeWhole(s, b)); continue; }
				List<GroupLibrary.Member> members = b != null ? Members(b) : null;
				if (members != null) yield return GroupLibrary.RegisterMembers(b.CatalogName, members, Category, b.Name);
			}
		}

		/// <summary>The scenes to load before these names can be spawned: those of the whole pieces not loaded yet (an island
		/// saved with the Vasagatan's hull).</summary>
		public static List<string> ScenesNeeded(IEnumerable<string> names)
		{
			return names.Where(n => IsWhole(n) && !PlaceableCatalog.IsLoaded(n)).Select(n => Find(n).Scene).Distinct().ToList();
		}

		static IEnumerator TakeWhole(UnityEngine.SceneManagement.Scene s, Building b)
		{
			string[] parts = b.Path.Split(new[] { '/' }, 2);
			GameObject root = s.GetRootGameObjects().FirstOrDefault(g => g.name == parts[0]);
			Transform node = root == null ? null : parts.Length > 1 ? root.transform.Find(parts[1]) : root.transform;
			if (node == null) Debug.LogWarning("[CUSTOM ISLANDS] Raft building '" + b.Name + "': " + b.Path + " not found in Raft's " + b.Scene + " scene");
			else PlaceableCatalog.AddWhole(b.CatalogName, node, Category, b.Name);
			yield break;
		}

		/// <summary>The Raft scenes a building's parts come from (for the object browser's hint).</summary>
		public static string SceneOf(string catalogName)
		{
			Building b = Find(catalogName);
			return b != null ? b.Scene : null;
		}
	}
}
