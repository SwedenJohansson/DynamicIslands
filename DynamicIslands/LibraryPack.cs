using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// What a library entry says about itself (info.json, the same in an exported .zip and in the island library on
	/// GitHub - _ProjectDocs\LIBRARY_TASKS.md). No game settings: the player's own New Game choices always apply.
	/// </summary>
	[Serializable]
	public class LibraryInfo
	{
		public string id = "", kind = LibraryPack.KindIsland, title = "", author = "", summary = "", description = "";
		public int version = 1;
		public string[] tags = new string[0];
		public string players = "1-8", length = "", icon = "", plan = "", minModVersion = "", created = "", basedOn = "";
		public string[] pictures = new string[0];
		public bool remix = true, featured = false;

		public bool IsPlan { get { return kind == LibraryPack.KindPlan; } }

		public string ToJson()
		{
			var o = new Dictionary<string, object>
			{
				{ "id", id }, { "kind", kind }, { "title", title }, { "author", author }, { "version", version }, { "summary", summary }, { "description", description },
				{ "tags", tags ?? new string[0] }, { "players", players }, { "length", length }, { "icon", icon }, { "pictures", pictures ?? new string[0] },
			};
			if (IsPlan) o["plan"] = plan;
			o["remix"] = remix;
			o["featured"] = featured;
			o["minModVersion"] = minModVersion;
			o["created"] = created;
			if (basedOn.Length > 0) o["basedOn"] = basedOn;
			return LibraryJson.Write(o);
		}

		/// <summary>An info from a JSON object (what's missing keeps its default).</summary>
		public static LibraryInfo From(Dictionary<string, object> o)
		{
			var d = new LibraryInfo();
			return new LibraryInfo
			{
				id = LibraryJson.Str(o, "id"), kind = LibraryJson.Str(o, "kind", d.kind), title = LibraryJson.Str(o, "title"), author = LibraryJson.Str(o, "author"),
				version = Math.Max(1, LibraryJson.Int(o, "version", 1)), summary = LibraryJson.Str(o, "summary"), description = LibraryJson.Str(o, "description"),
				tags = LibraryJson.Strings(o, "tags"), players = LibraryJson.Str(o, "players", d.players), length = LibraryJson.Str(o, "length"), icon = LibraryJson.Str(o, "icon"),
				pictures = LibraryJson.Strings(o, "pictures"), plan = LibraryJson.Str(o, "plan"), remix = LibraryJson.Bool(o, "remix", true), featured = LibraryJson.Bool(o, "featured"),
				minModVersion = LibraryJson.Str(o, "minModVersion"), created = LibraryJson.Str(o, "created"), basedOn = LibraryJson.Str(o, "basedOn"),
			};
		}

		public static LibraryInfo FromJson(string json)
		{
			var o = LibraryJson.Parse(json) as Dictionary<string, object>;
			if (o == null) throw new FormatException("not a JSON object");
			return From(o);
		}
	}

	/// <summary>One installed (downloaded or imported) entry, as library\installed.json remembers it.</summary>
	[Serializable]
	public class LibraryInstalled
	{
		public string id = "", source = LibraryPack.SourceImport, title = "", author = "", kind = LibraryPack.KindIsland, date = "", plan = "";
		public int version = 1;
		public bool remix = true;
		public List<LibraryInstalledFile> files = new List<LibraryInstalledFile>();

		public string Source { get { return source + ":" + id + "@" + version.ToString(CultureInfo.InvariantCulture); } }

		public Dictionary<string, object> ToDict()
		{
			return new Dictionary<string, object>
			{
				{ "id", id }, { "source", source }, { "title", title }, { "author", author }, { "kind", kind }, { "version", version }, { "remix", remix }, { "date", date }, { "plan", plan },
				{ "files", files.Select(f => (object)new Dictionary<string, object> { { "name", f.name }, { "original", f.original }, { "kind", f.kind }, { "sha256", f.sha256 }, { "shared", f.shared } }).ToList() },
			};
		}

		public static LibraryInstalled From(Dictionary<string, object> o)
		{
			return new LibraryInstalled
			{
				id = LibraryJson.Str(o, "id"), source = LibraryJson.Str(o, "source", LibraryPack.SourceImport), title = LibraryJson.Str(o, "title"), author = LibraryJson.Str(o, "author"),
				kind = LibraryJson.Str(o, "kind", LibraryPack.KindIsland), version = LibraryJson.Int(o, "version", 1), remix = LibraryJson.Bool(o, "remix", true), date = LibraryJson.Str(o, "date"), plan = LibraryJson.Str(o, "plan"),
				files = LibraryJson.Objects(o, "files").Select(f => new LibraryInstalledFile { name = LibraryJson.Str(f, "name"), original = LibraryJson.Str(f, "original"), kind = LibraryJson.Str(f, "kind", LibraryPack.KindIsland), sha256 = LibraryJson.Str(f, "sha256"), shared = LibraryJson.Bool(f, "shared") }).ToList(),
			};
		}
	}

	public class LibraryInstalledFile
	{
		/// <summary>The file's name here (an island's name, or the plan's), what the entry called it, and its SHA-256 as written.</summary>
		public string name = "", original = "", sha256 = "", kind = "island";
		/// <summary>True when the same file was already here (the player's own, or another entry's): never removed with this entry.</summary>
		public bool shared;
	}

	/// <summary>A pack read from a .zip (checked): its info and its files by name.</summary>
	public class LibraryPackContents
	{
		public LibraryInfo Info;
		public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		public string Path;

		public IEnumerable<string> IslandNames { get { return Files.Keys.Where(f => f.EndsWith(IslandFile.Extension, StringComparison.OrdinalIgnoreCase)).Select(f => f.Substring(0, f.Length - IslandFile.Extension.Length)); } }
		/// <summary>Made with a newer version of the mod than this one (Q5: the player is warned and chooses).</summary>
		public bool Newer { get { return Info != null && LibraryPack.CompareVersions(Info.minModVersion, LibraryPack.ModVersion) > 0; } }
	}

	/// <summary>
	/// Islands and world plans as files to share (LIBRARY_TASKS.md, A): a pack is a .zip holding one folder named after the
	/// entry's id, laid out like a folder of the island library - info.json, icon.jpg, pictures, the .island files and for a
	/// plan its .plan. Export collects everything an island or a plan needs (the islands its rules bring, and theirs);
	/// import checks the pack strictly (sizes, names, file formats) and installs it without ever overwriting the player's
	/// own files: an island whose name is taken by a different one is installed as "Name (Entry title)", and the entry's
	/// plan and islands are rewritten to use that name. installed.json remembers what each entry wrote, so it can be
	/// updated (worlds in progress keep the version they started with) and removed (never what a saved world still uses).
	/// </summary>
	public static class LibraryPack
	{
		public const string KindIsland = "island", KindPlan = "plan", SourceImport = "import", SourceLibrary = "library";
		public const long MaxTotalBytes = 50L * 1024 * 1024;
		public const int MaxFiles = 64;
		static readonly string[] AllowedExtensions = { IslandFile.Extension, WorldPlan.Extension, ".json", ".jpg", ".jpeg", ".png" };

		public static string LibraryFolder { get { return Path.Combine(DynamicIslands.assetpath, "library"); } }
		public static string ExportFolder { get { return Path.Combine(DynamicIslands.assetpath, "exports"); } }
		public static string ImportFolder { get { return Path.Combine(DynamicIslands.assetpath, "import"); } }
		static string InstalledPath { get { return Path.Combine(LibraryFolder, "installed.json"); } }
		static string WorldsFolder { get { return Path.Combine(DynamicIslands.assetpath, "worlds"); } }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [library] " + msg); }

		#region Small helpers

		/// <summary>The mod's version (modinfo.json).</summary>
		public static string ModVersion { get { return TestVersion ?? ExperimentalNotice.Version; } }
		/// <summary>Dev tests (CIFakeVersion): this PC pretends to have another version of the mod.</summary>
		public static string TestVersion;

		/// <summary>Compares versions as numbers ("3.10" is newer than "3.9"); an empty or odd one counts as 0.</summary>
		public static int CompareVersions(string a, string b)
		{
			int[] x = Parts(a), y = Parts(b);
			for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
			{
				int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
				if (p != q) return p.CompareTo(q);
			}
			return 0;
		}

		static int[] Parts(string v)
		{
			return (v ?? "").Split('.').Select(s => { int n; return int.TryParse(new string(s.TakeWhile(char.IsDigit).ToArray()), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0; }).ToArray();
		}

		/// <summary>An id from a title: lower case letters, digits and - (e.g. "First Voyage!" -> "first-voyage").</summary>
		public static string IdFrom(string title)
		{
			var sb = new StringBuilder();
			foreach (char c in (title ?? "").ToLowerInvariant())
			{
				if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
				else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
			}
			string id = sb.ToString().Trim('-');
			if (id.Length > 48) id = id.Substring(0, 48).Trim('-');
			if (FileNames.IsReserved(id)) id += "-entry"; // (a title "Con" or "Aux" would be a zip Windows can't write)
			return id.Length > 0 ? id : "entry";
		}

		/// <summary>A name that is safe as a file name here and in a download address (no folders, no # % ?, nothing Windows refuses).</summary>
		public static bool IsSafeFileName(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Length > 120 || name != name.Trim() || name.StartsWith(".") || name.Contains("..")) return false;
			if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '#', '%', '?', '/', '\\', ':' }) >= 0) return false;
			// (Windows' device names, also with an extension - FileNames)
			return !FileNames.IsReserved(name);
		}

		public static string Sha256(byte[] bytes)
		{
			using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
		}

		static string ShaOfFile(string path) { return File.Exists(path) ? Sha256(File.ReadAllBytes(path)) : null; }

		/// <summary>The format number of an island file's bytes (-1 if it isn't one).</summary>
		public static int IslandFormat(byte[] b)
		{
			if (b == null || b.Length < 8 || b[0] != 'C' || b[1] != 'I' || b[2] != 'S' || b[3] != 'L') return -1;
			return BitConverter.ToInt32(b, 4);
		}

		#endregion

		#region What an island or a plan needs

		/// <summary>The island names a rule brings (island:, oneof:).</summary>
		public static IEnumerable<string> IslandsOf(IntroRule r)
		{
			if (r.What == "island") return new[] { r.WhatArg.Trim() };
			if (r.What == "oneof") return r.WhatArg.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0);
			return new string[0];
		}

		/// <summary>
		/// Every island the start islands and the plan's rules bring, and the islands those bring, and so on (the islands'
		/// own "bring" rules). missing: named but not saved here.
		/// </summary>
		public static List<string> Collect(IEnumerable<string> start, WorldPlan plan, out List<string> missing)
		{
			var found = new List<string>();
			missing = new List<string>();
			var queue = new Queue<string>(start ?? new string[0]);
			if (plan != null) foreach (IntroRule r in plan.Rules) foreach (string n in IslandsOf(r)) queue.Enqueue(n);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			while (queue.Count > 0)
			{
				string n = queue.Dequeue();
				if (n.Length == 0 || !seen.Add(n)) continue;
				if (!File.Exists(IslandSpawner.PathFor(n))) { missing.Add(n); continue; }
				found.Add(n);
				foreach (IntroRule r in WorldDirector.RulesFromProps(IslandCache.Props(n)))
					foreach (string m in IslandsOf(r)) queue.Enqueue(m);
			}
			return found;
		}

		#endregion

		#region Export

		static string LastExportsPath { get { return Path.Combine(ExportFolder, "exports.json"); } }

		/// <summary>exports.json: { "items": [ { "key": "plan:First Voyage", "info": { ... } } ] }.</summary>
		static List<Dictionary<string, object>> LastExports()
		{
			if (!File.Exists(LastExportsPath)) return new List<Dictionary<string, object>>();
			return LibraryJson.Objects(LibraryJson.Parse(File.ReadAllText(LastExportsPath)) as Dictionary<string, object>, "items");
		}

		/// <summary>
		/// What was filled in the last time this island or plan was exported (so a new export keeps its id and raises its
		/// version - an update of the same entry, not a new one), or a fresh info.
		/// </summary>
		public static LibraryInfo LastInfo(string kind, string name)
		{
			try
			{
				Dictionary<string, object> e = LastExports().FirstOrDefault(x => LibraryJson.Str(x, "key").Equals(kind + ":" + name, StringComparison.OrdinalIgnoreCase));
				object infoObj;
				if (e != null && e.TryGetValue("info", out infoObj) && infoObj is Dictionary<string, object>)
				{
					LibraryInfo last = LibraryInfo.From((Dictionary<string, object>)infoObj);
					last.version++;
					return last;
				}
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + LastExportsPath + ": " + ex.Message); }
			var info = new LibraryInfo { kind = kind, title = name, id = IdFrom(name), plan = kind == KindPlan ? name + WorldPlan.Extension : "" };
			// (an island or plan that came from someone else's entry: credit them)
			LibraryInstalled from = OwnerOf(kind == KindPlan ? name + WorldPlan.Extension : name + IslandFile.Extension);
			if (from != null) { info.basedOn = from.title + " by " + from.author; info.remix = from.remix; }
			return info;
		}

		static void RememberExport(string kind, string name, LibraryInfo info)
		{
			try
			{
				List<Dictionary<string, object>> items = LastExports();
				items.RemoveAll(x => LibraryJson.Str(x, "key").Equals(kind + ":" + name, StringComparison.OrdinalIgnoreCase));
				items.Add(new Dictionary<string, object> { { "key", kind + ":" + name }, { "info", LibraryJson.Parse(info.ToJson()) } });
				Directory.CreateDirectory(ExportFolder);
				File.WriteAllText(LastExportsPath, LibraryJson.Write(new Dictionary<string, object> { { "items", items } }));
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Could not remember the export: " + ex.Message); }
		}

		/// <summary>
		/// Exports an island (with every island its rules bring) or a plan (with every island it needs) as a .zip in
		/// Mods\DynamicIslands\exports. icon / picture: JPG bytes (null = none). Returns the zip's path, or null and why not.
		/// </summary>
		public static string Export(LibraryInfo info, string islandName, WorldPlan plan, byte[] icon, byte[] picture, out string error) { return ExportWith(info, islandName, plan, icon, picture != null ? new[] { picture } : new byte[0][], out error); }

		/// <summary>The same with up to 4 pictures (picture1.jpg ...).</summary>
		public static string ExportWith(LibraryInfo info, string islandName, WorldPlan plan, byte[] icon, IList<byte[]> pictures, out string error)
		{
			error = null;
			try
			{
				if (info.title.Trim().Length == 0) { error = "Give it a title."; return null; }
				if (info.author.Trim().Length == 0) { error = "Say who made it (the author)."; return null; }
				info.id = IdFrom(info.id.Length > 0 ? info.id : info.title);
				info.kind = plan != null ? KindPlan : KindIsland;
				info.minModVersion = ModVersion;
				if (info.created.Length == 0) info.created = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

				List<string> missing;
				List<string> islands = Collect(islandName != null ? new[] { islandName } : new string[0], plan, out missing);
				if (missing.Count > 0) { error = "These islands are named by its rules but aren't saved here: " + string.Join(", ", missing.ToArray()) + "."; return null; }
				if (islands.Count == 0 && plan == null) { error = "There is no saved island '" + islandName + "'."; return null; }
				// (remixes: an island that came from someone else's entry which says no)
				foreach (string n in islands)
				{
					LibraryInstalled from = OwnerOf(n + IslandFile.Extension);
					if (from != null && !from.remix && !from.author.Equals(info.author.Trim(), StringComparison.OrdinalIgnoreCase))
					{ error = "'" + n + "' comes from '" + from.title + "' by " + from.author + ", who asked for it not to be shared changed (no remixes)."; return null; }
				}
				var files = new List<KeyValuePair<string, byte[]>>();
				foreach (string n in islands)
				{
					if (!IsSafeFileName(n + IslandFile.Extension)) { error = "The island name '" + n + "' can't be shared as a file name (no # % ? / \\ :). Save it under another name first."; return null; }
					files.Add(new KeyValuePair<string, byte[]>(n + IslandFile.Extension, File.ReadAllBytes(IslandSpawner.PathFor(n))));
				}
				if (plan != null)
				{
					if (!IsSafeFileName(plan.Name + WorldPlan.Extension)) { error = "The plan name '" + plan.Name + "' can't be shared as a file name (no # % ? / \\ :)."; return null; }
					info.plan = plan.Name + WorldPlan.Extension;
					files.Add(new KeyValuePair<string, byte[]>(info.plan, Encoding.UTF8.GetBytes(plan.ToText())));
				}
				else info.plan = "";
				info.icon = icon != null ? "icon.jpg" : "";
				pictures = (pictures ?? new byte[0][]).Where(p => p != null).Take(4).ToList();
				info.pictures = Enumerable.Range(1, pictures.Count).Select(i => "picture" + i + ".jpg").ToArray();
				if (icon != null) files.Add(new KeyValuePair<string, byte[]>("icon.jpg", icon));
				for (int i = 0; i < pictures.Count; i++) files.Add(new KeyValuePair<string, byte[]>("picture" + (i + 1) + ".jpg", pictures[i]));
				files.Insert(0, new KeyValuePair<string, byte[]>("info.json", Encoding.UTF8.GetBytes(info.ToJson())));
				if (files.Sum(f => (long)f.Value.Length) > MaxTotalBytes) { error = "It is over " + (MaxTotalBytes / 1024 / 1024) + " MB - too big to share."; return null; }

				Directory.CreateDirectory(ExportFolder);
				string path = Path.Combine(ExportFolder, info.id + ".zip");
				WriteZip(path, info.id, files);
				RememberExport(info.kind, plan != null ? plan.Name : islandName, info);
				Log("Exported " + info.kind + " '" + info.title + "' (" + info.id + ", version " + info.version + "): " + islands.Count + " island(s) -> " + path);
				return path;
			}
			catch (Exception e) { error = "Exporting failed: " + e.Message; Debug.LogWarning("[CUSTOM ISLANDS] " + e); return null; }
		}

		static void WriteZip(string path, string folder, IEnumerable<KeyValuePair<string, byte[]>> files)
		{
			string tmp = path + ".tmp";
			using (var zip = new ZipOutputStream(File.Create(tmp)))
			{
				zip.SetLevel(6);
				foreach (var f in files)
				{
					var e = new ZipEntry(folder + "/" + f.Key) { DateTime = DateTime.Now, Size = f.Value.Length, IsUnicodeText = true };
					zip.PutNextEntry(e);
					zip.Write(f.Value, 0, f.Value.Length);
					zip.CloseEntry();
				}
				zip.Finish();
			}
			if (File.Exists(path)) File.Delete(path);
			File.Move(tmp, path);
		}

		#endregion

		#region Reading a pack

		/// <summary>
		/// Reads and checks a pack: at most 64 files and 50 MB unpacked (counted while unpacking, whatever the zip claims),
		/// plain file names only (one folder at most, no "..", nothing that could land outside the mod's folder), known kinds
		/// of files, a valid info.json, island files this version can read, the plan and every island it names inside.
		/// Returns null and why when something is wrong; nothing is written.
		/// </summary>
		public static LibraryPackContents Read(string path, out string error)
		{
			error = null;
			var pack = new LibraryPackContents { Path = path };
			try
			{
				long total = 0;
				string folder = null;
				using (var zip = new ZipInputStream(File.OpenRead(path)))
				{
					ZipEntry e;
					var buffer = new byte[65536];
					while ((e = zip.GetNextEntry()) != null)
					{
						if (e.IsDirectory) continue;
						string full = (e.Name ?? "").Replace('\\', '/');
						string[] parts = full.Split('/');
						if (parts.Length > 2 || parts.Any(p => p == ".." || p == ".")) { error = "It holds a file in a place it may not ('" + full + "')."; return null; }
						string top = parts.Length == 2 ? parts[0] : "";
						if (folder == null) folder = top;
						else if (!folder.Equals(top, StringComparison.OrdinalIgnoreCase)) { error = "It holds more than one entry's folder."; return null; }
						string name = parts[parts.Length - 1];
						if (!IsSafeFileName(name)) { error = "It holds a file with a name that isn't allowed ('" + name + "')."; return null; }
						if (!AllowedExtensions.Any(x => name.EndsWith(x, StringComparison.OrdinalIgnoreCase))) { error = "It holds a kind of file that doesn't belong in an island pack ('" + name + "')."; return null; }
						if (pack.Files.Count >= MaxFiles) { error = "It holds more than " + MaxFiles + " files."; return null; }
						if (pack.Files.ContainsKey(name)) { error = "It holds '" + name + "' twice."; return null; }
						using (var ms = new MemoryStream())
						{
							int n;
							while ((n = zip.Read(buffer, 0, buffer.Length)) > 0)
							{
								total += n;
								if (total > MaxTotalBytes) { error = "It unpacks to more than " + (MaxTotalBytes / 1024 / 1024) + " MB."; return null; }
								ms.Write(buffer, 0, n);
							}
							pack.Files[name] = ms.ToArray();
						}
					}
				}
				byte[] infoBytes;
				if (!pack.Files.TryGetValue("info.json", out infoBytes)) { error = "It has no info.json - it isn't an island pack."; return null; }
				LibraryInfo info;
				try { info = LibraryInfo.FromJson(Encoding.UTF8.GetString(infoBytes).TrimStart('﻿')); }
				catch (Exception e) { error = "Its info.json can't be read (" + e.Message + ")."; return null; }
				info.id = IdFrom(info.id.Length > 0 ? info.id : (folder ?? "").Length > 0 ? folder : info.title);
				pack.Info = info;
				return Validate(pack, out error) ? pack : null;
			}
			catch (Exception e) { error = "It can't be read as a pack (" + e.Message + ")."; return null; }
		}

		/// <summary>
		/// A pack from files downloaded from the island library (their SHA-256 checked already), checked the same way as a
		/// .zip: plain names, known kinds of files, the size limits, island files this version reads, the plan's islands.
		/// </summary>
		public static LibraryPackContents FromFiles(LibraryInfo info, Dictionary<string, byte[]> files, out string error)
		{
			error = null;
			var pack = new LibraryPackContents { Info = info, Path = "library:" + info.id };
			if (files.Count > MaxFiles) { error = "It holds more than " + MaxFiles + " files."; return null; }
			if (files.Values.Sum(b => (long)b.Length) > MaxTotalBytes) { error = "It is more than " + (MaxTotalBytes / 1024 / 1024) + " MB."; return null; }
			foreach (var f in files)
			{
				if (!IsSafeFileName(f.Key)) { error = "It holds a file with a name that isn't allowed ('" + f.Key + "')."; return null; }
				if (!AllowedExtensions.Any(x => f.Key.EndsWith(x, StringComparison.OrdinalIgnoreCase))) { error = "It holds a kind of file that doesn't belong in an island pack ('" + f.Key + "')."; return null; }
				pack.Files[f.Key] = f.Value;
			}
			info.id = IdFrom(info.id.Length > 0 ? info.id : info.title);
			return Validate(pack, out error) ? pack : null;
		}

		/// <summary>The checks every pack gets, however it came: its info, island files this version reads, a plan's islands.</summary>
		static bool Validate(LibraryPackContents pack, out string error)
		{
			error = null;
			LibraryInfo info = pack.Info;
			if (info.title.Trim().Length == 0) info.title = info.id;
			if (info.kind != KindIsland && info.kind != KindPlan) { error = "Its info.json says kind '" + info.kind + "' (island or plan)."; return false; }
			foreach (string n in pack.Files.Keys.Where(f => f.EndsWith(IslandFile.Extension, StringComparison.OrdinalIgnoreCase)).ToList())
			{
				int format = IslandFormat(pack.Files[n]);
				if (format < 0) { error = "'" + n + "' isn't an island file."; return false; }
				if (format > IslandFile.FormatVersion) { error = "'" + n + "' was saved by a newer Custom Islands than this one - update the mod to use it."; return false; }
			}
			if (!pack.IslandNames.Any()) { error = "It holds no island."; return false; }
			if (info.IsPlan)
			{
				byte[] planBytes;
				if (info.plan.Length == 0 || !pack.Files.TryGetValue(info.plan, out planBytes)) { error = "Its plan '" + info.plan + "' isn't in it."; return false; }
				WorldPlan plan = WorldPlan.Parse(Path.GetFileNameWithoutExtension(info.plan), Encoding.UTF8.GetString(planBytes));
				var names = new HashSet<string>(pack.IslandNames, StringComparer.OrdinalIgnoreCase);
				string absent = plan.Rules.SelectMany(IslandsOf).FirstOrDefault(n => !names.Contains(n));
				if (absent != null) { error = "Its plan brings the island '" + absent + "', which isn't in it."; return false; }
			}
			return true;
		}

		#endregion

		#region Installed entries

		public static List<LibraryInstalled> Installed()
		{
			try
			{
				if (!File.Exists(InstalledPath)) return new List<LibraryInstalled>();
				return LibraryJson.Objects(LibraryJson.Parse(File.ReadAllText(InstalledPath)) as Dictionary<string, object>, "entries").Select(LibraryInstalled.From).ToList();
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read " + InstalledPath + ": " + e.Message); return new List<LibraryInstalled>(); }
		}

		/// <summary>Writes installed.json (public for tests).</summary>
		public static void SaveInstalled(List<LibraryInstalled> entries)
		{
			Directory.CreateDirectory(LibraryFolder);
			SafeFile.WriteAllText(InstalledPath, LibraryJson.Write(new Dictionary<string, object> { { "entries", entries.Select(e => (object)e.ToDict()).ToList() } }));
		}

		/// <summary>The entry that wrote this file (an island "Name.island" or a plan "Name.plan"), if one did.</summary>
		public static LibraryInstalled OwnerOf(string fileName)
		{
			return Installed().FirstOrDefault(e => e.files.Any(f => !f.shared && (f.name + (f.kind == KindPlan ? WorldPlan.Extension : IslandFile.Extension)).Equals(fileName, StringComparison.OrdinalIgnoreCase)));
		}

		/// <summary>An installed entry's islands and plan the player has changed since (saved over in the editor or World Plans).</summary>
		public static List<string> ChangedFiles(string id)
		{
			var result = new List<string>();
			LibraryInstalled e = Installed().FirstOrDefault(x => x.id.Equals(id, StringComparison.OrdinalIgnoreCase));
			if (e == null) return result;
			foreach (LibraryInstalledFile f in e.files.Where(f => !f.shared))
			{
				string path = f.kind == KindPlan ? WorldPlan.PathFor(f.name) : IslandSpawner.PathFor(f.name);
				try { if (File.Exists(path) && ShaOfFile(path) != f.sha256) result.Add(f.name); } catch { }
			}
			return result;
		}

		#endregion

		#region Worlds using an island

		/// <summary>The saved worlds (their names) whose island list has this island, or whose plan is this plan without a copy of it.</summary>
		public static List<string> WorldsUsing(string island, string plan = null)
		{
			var result = new List<string>();
			try
			{
				if (!Directory.Exists(WorldsFolder)) return result;
				foreach (string file in Directory.GetFiles(WorldsFolder, "*.txt"))
				{
					string[] lines = File.ReadAllLines(file);
					bool uses = island != null && lines.Any(l => !l.StartsWith("@") && !l.StartsWith("#") && l.Split('|')[0].Trim().Equals(island, StringComparison.OrdinalIgnoreCase));
					if (!uses && plan != null)
						uses = lines.Any(l => l.Trim().Equals("@plan=" + plan, StringComparison.OrdinalIgnoreCase)) && !lines.Any(l => l.StartsWith("@planrule="));
					if (!uses) continue;
					string head = lines.FirstOrDefault(l => l.StartsWith("# Custom islands in world '"));
					result.Add(head != null && head.IndexOf('\'') >= 0 ? head.Substring(head.IndexOf('\'') + 1).Split('\'')[0] : Path.GetFileNameWithoutExtension(file));
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Looking through the saved worlds: " + e.Message); }
			return result.Distinct().ToList();
		}

		#endregion

		#region Installing

		/// <summary>What an install did, for the player.</summary>
		public class Report
		{
			public readonly List<string> Lines = new List<string>();
			public string PlanName;
			public List<string> Islands = new List<string>();
			public void Add(string line) { Lines.Add(line); Log(line); }
			public override string ToString() { return string.Join("\n", Lines.ToArray()); }
		}

		/// <summary>
		/// Installs a pack (checked by Read). appearWhileSailing: an island entry's islands also turn up by chance while
		/// sailing (spawnpool.txt weight 1, else 0; a plan's islands always 0). replaceChanged: an earlier version's file the
		/// player has changed since is replaced too (else kept). source: SourceImport or SourceLibrary.
		/// </summary>
		public static Report Install(LibraryPackContents pack, bool appearWhileSailing, bool replaceChanged, string source)
		{
			var report = new Report();
			LibraryInfo info = pack.Info;
			List<LibraryInstalled> all = Installed();
			LibraryInstalled old = all.FirstOrDefault(e => e.id.Equals(info.id, StringComparison.OrdinalIgnoreCase));
			var entry = new LibraryInstalled { id = info.id, source = source, title = info.title, author = info.author, kind = info.kind, version = info.version, remix = info.remix, date = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) };
			report.Add((old != null ? "Updating '" : "Installing '") + info.title + "' by " + info.author + " (version " + info.version + (old != null ? ", was " + old.version : "") + ")");

			// Where each island goes: its own name if free or the same file is here, the name it had in the earlier version,
			// else "Name (Title)" - and the entry's rules follow the new names (which can change files, so until it settles)
			var originals = pack.IslandNames.ToList();
			var target = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string n in originals)
			{
				LibraryInstalledFile before = old != null ? old.files.FirstOrDefault(f => f.kind == KindIsland && f.original.Equals(n, StringComparison.OrdinalIgnoreCase)) : null;
				target[n] = before != null && !before.shared ? before.name : n;
			}
			var content = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
			for (int round = 0; round < 8; round++)
			{
				bool changed = false;
				foreach (string n in originals)
				{
					byte[] bytes = Rewritten(pack.Files[n + IslandFile.Extension], originals, target);
					content[n] = bytes;
					string t = target[n];
					string path = IslandSpawner.PathFor(t);
					bool ownsIt = old != null && old.files.Any(f => !f.shared && f.name.Equals(t, StringComparison.OrdinalIgnoreCase));
					bool takenByOther = originals.Any(o => o != n && target[o].Equals(t, StringComparison.OrdinalIgnoreCase));
					if (takenByOther || (File.Exists(path) && !ownsIt && Sha256(File.ReadAllBytes(path)) != Sha256(bytes)))
					{
						target[n] = FreeName(n, info.title, originals, target);
						changed = true;
					}
				}
				if (!changed) break;
			}

			// The islands
			foreach (string n in originals)
			{
				string t = target[n];
				string path = IslandSpawner.PathFor(t);
				byte[] bytes = content[n];
				string sha = Sha256(bytes);
				LibraryInstalledFile before = old != null ? old.files.FirstOrDefault(f => !f.shared && f.name.Equals(t, StringComparison.OrdinalIgnoreCase)) : null;
				if (File.Exists(path) && ShaOfFile(path) == sha)
				{
					entry.files.Add(new LibraryInstalledFile { name = t, original = n, sha256 = sha, kind = KindIsland, shared = before == null });
					report.Add(before != null ? "'" + t + "' is unchanged" : "'" + t + "' is already here (the same island) - shared");
				}
				else
				{
					if (File.Exists(path) && before != null)
					{
						string current = ShaOfFile(path);
						if (current != before.sha256 && !replaceChanged)
						{
							entry.files.Add(new LibraryInstalledFile { name = t, original = n, sha256 = current, kind = KindIsland });
							report.Add("Kept your changed '" + t + "' (the new version's is not installed)");
							continue;
						}
						// (a saved world plays the version it started with: its old file stays as the copy that world's hash finds)
						KeepForWorlds(t, report);
					}
					SafeFile.WriteAllBytes(path, bytes);
					entry.files.Add(new LibraryInstalledFile { name = t, original = n, sha256 = sha, kind = KindIsland });
					report.Add(t.Equals(n, StringComparison.OrdinalIgnoreCase) ? "Installed '" + t + "'" : "Installed '" + n + "' as '" + t + "' (you have a different island called '" + n + "')");
				}
				report.Islands.Add(t);
			}

			// The plan
			if (info.IsPlan)
			{
				WorldPlan plan = WorldPlan.Parse(Path.GetFileNameWithoutExtension(info.plan), Encoding.UTF8.GetString(pack.Files[info.plan]));
				foreach (IntroRule r in plan.Rules) Rename(r, target);
				LibraryInstalledFile before = old != null ? old.files.FirstOrDefault(f => f.kind == KindPlan && !f.shared) : null;
				string name = before != null ? before.name : plan.Name;
				string text = plan.ToText();
				if (before == null)
				{
					string existing = WorldPlan.PathFor(name);
					if (WorldPlan.IsBuiltIn(name) || (File.Exists(existing) && File.ReadAllText(existing) != text))
					{
						name = FreePlanName(plan.Name, info.author);
						report.Add("Installed the plan '" + plan.Name + "' as '" + name + "' (you have a different plan called '" + plan.Name + "')");
					}
					else report.Add("Installed the plan '" + name + "'");
				}
				else report.Add("Updated the plan '" + name + "' (worlds already started keep their own copy of it)");
				plan.Name = name;
				Directory.CreateDirectory(WorldPlan.Folder);
				SafeFile.WriteAllText(WorldPlan.PathFor(name), text);
				entry.plan = name;
				entry.files.Add(new LibraryInstalledFile { name = name, original = Path.GetFileNameWithoutExtension(info.plan), sha256 = Sha256(Encoding.UTF8.GetBytes(text)), kind = KindPlan });
				report.PlanName = name;
			}

			// An earlier version's files the new one doesn't have any more
			if (old != null)
				foreach (LibraryInstalledFile f in old.files.Where(f => !f.shared && !entry.files.Any(x => x.name.Equals(f.name, StringComparison.OrdinalIgnoreCase) && x.kind == f.kind)))
					RemoveFile(f, entry.id, all, report);

			// Random islands while sailing: a plan's islands never, an island entry's as the player chose (their own islands untouched)
			foreach (LibraryInstalledFile f in entry.files.Where(f => f.kind == KindIsland && !f.shared))
				SetPoolWeight(f.name, !info.IsPlan && appearWhileSailing ? 1f : 0f);

			all.RemoveAll(e => e.id.Equals(info.id, StringComparison.OrdinalIgnoreCase));
			all.Add(entry);
			SaveInstalled(all);
			IslandCache.Forget();
			return report;
		}

		/// <summary>An island file's bytes with its "bring" rules pointing at the entry's new names (unchanged if none moved).</summary>
		static byte[] Rewritten(byte[] bytes, List<string> originals, Dictionary<string, string> target)
		{
			if (!originals.Any(n => !target[n].Equals(n, StringComparison.Ordinal))) return bytes;
			string tmp = Path.Combine(LibraryFolder, "rewrite.tmp" + IslandFile.Extension);
			Directory.CreateDirectory(LibraryFolder);
			try
			{
				File.WriteAllBytes(tmp, bytes);
				IslandFile f = IslandFile.Load(tmp);
				string text;
				if (f.Props == null || !f.Props.TryGetValue(WorldDirector.IslandRulesKey, out text)) return bytes;
				List<IntroRule> rules = IntroRule.ParseLines(text);
				string before = IntroRule.ToLines(rules);
				foreach (IntroRule r in rules) Rename(r, target);
				if (IntroRule.ToLines(rules) == before) return bytes;
				WorldDirector.SetRulesInProps(f.Props, rules);
				f.Save(tmp);
				return File.ReadAllBytes(tmp);
			}
			finally { try { File.Delete(tmp); } catch { } }
		}

		/// <summary>A rule's island names (what it brings, and the islands it waits for or is placed near) after renames.</summary>
		static void Rename(IntroRule r, Dictionary<string, string> target)
		{
			Func<string, string> to = n => { string t; return target.TryGetValue(n.Trim(), out t) ? t : n.Trim(); };
			if (r.What == "island") r.WhatArg = to(r.WhatArg);
			else if (r.What == "oneof") r.WhatArg = string.Join(", ", r.WhatArg.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).Select(to).ToArray());
			if (r.WhenRef.Length > 0) r.WhenRef = to(r.WhenRef);
			if (r.WhereRef.Length > 0) r.WhereRef = to(r.WhereRef);
		}

		static string FreeName(string name, string title, List<string> originals, Dictionary<string, string> target)
		{
			string clean = new string((title ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c != '#' && c != '%' && c != '?').ToArray()).Trim();
			if (clean.Length == 0) clean = "library";
			for (int i = 1; ; i++)
			{
				string candidate = name + " (" + clean + ")" + (i > 1 ? " " + i : "");
				bool used = File.Exists(IslandSpawner.PathFor(candidate)) || originals.Any(o => target[o].Equals(candidate, StringComparison.OrdinalIgnoreCase));
				if (!used) return candidate;
			}
		}

		static string FreePlanName(string name, string author)
		{
			for (int i = 1; ; i++)
			{
				string candidate = name + " (" + (author.Trim().Length > 0 ? author.Trim() : "library") + ")" + (i > 1 ? " " + i : "");
				if (!File.Exists(WorldPlan.PathFor(candidate)) && !WorldPlan.IsBuiltIn(candidate)) return candidate;
			}
		}

		/// <summary>Before an island file is replaced: saved worlds that use it keep playing this version (as its hash copy).</summary>
		static void KeepForWorlds(string island, Report report)
		{
			List<string> worlds = WorldsUsing(island);
			string hash = IslandNetwork.HashOf(island);
			if (worlds.Count == 0 || hash == null) return;
			string copy = IslandSpawner.PathFor(IslandNetwork.DownloadName(island, hash));
			if (!File.Exists(copy)) File.Copy(IslandSpawner.PathFor(island), copy);
			report.Add("Kept the version of '" + island + "' that " + string.Join(", ", worlds.Select(w => "'" + w + "'").ToArray()) + " started with");
		}

		/// <summary>Sets an island's weight in spawnpool.txt (0 = never turns up by chance while sailing).</summary>
		public static void SetPoolWeight(string island, float weight)
		{
			try
			{
				string path = Path.Combine(DynamicIslands.assetpath, CustomIslandSpawner.PoolFileName);
				CustomIslandSpawner.LoadPool(false); // (makes the file if it isn't there)
				var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
				string line = island + " " + weight.ToString(CultureInfo.InvariantCulture);
				int i = lines.FindIndex(l => PoolLineName(l).Equals(island, StringComparison.OrdinalIgnoreCase));
				if (i >= 0) lines[i] = line; else lines.Add(line);
				File.WriteAllLines(path, lines.ToArray());
				CustomIslandSpawner.LoadPool(true);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not change spawnpool.txt: " + e.Message); }
		}

		static void RemovePoolLine(string island)
		{
			try
			{
				string path = Path.Combine(DynamicIslands.assetpath, CustomIslandSpawner.PoolFileName);
				if (!File.Exists(path)) return;
				var lines = File.ReadAllLines(path).ToList();
				if (lines.RemoveAll(l => PoolLineName(l).Equals(island, StringComparison.OrdinalIgnoreCase)) > 0) { File.WriteAllLines(path, lines.ToArray()); CustomIslandSpawner.LoadPool(true); }
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not change spawnpool.txt: " + e.Message); }
		}

		static string PoolLineName(string line)
		{
			string l = line.Trim();
			if (l.Length == 0 || l.StartsWith("#") || l.Contains("=")) return "";
			int sp = l.LastIndexOf(' ');
			float w;
			return sp > 0 && float.TryParse(l.Substring(sp + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out w) ? l.Substring(0, sp).Trim() : l;
		}

		#endregion

		#region Removing

		/// <summary>Removes an installed entry's files - never one that is shared or that a saved world still uses.</summary>
		public static Report Remove(string id)
		{
			var report = new Report();
			List<LibraryInstalled> all = Installed();
			LibraryInstalled entry = all.FirstOrDefault(e => e.id.Equals(id, StringComparison.OrdinalIgnoreCase));
			if (entry == null) { report.Add("Nothing called '" + id + "' is installed"); return report; }
			report.Add("Removing '" + entry.title + "' by " + entry.author);
			foreach (LibraryInstalledFile f in entry.files.Where(f => !f.shared)) RemoveFile(f, entry.id, all, report);
			all.Remove(entry);
			SaveInstalled(all);
			IslandCache.Forget();
			return report;
		}

		static void RemoveFile(LibraryInstalledFile f, string id, List<LibraryInstalled> all, Report report)
		{
			bool plan = f.kind == KindPlan;
			string path = plan ? WorldPlan.PathFor(f.name) : IslandSpawner.PathFor(f.name);
			if (!File.Exists(path)) return;
			if (all.Any(e => !e.id.Equals(id, StringComparison.OrdinalIgnoreCase) && e.files.Any(x => x.kind == f.kind && x.name.Equals(f.name, StringComparison.OrdinalIgnoreCase))))
			{ report.Add("Kept '" + f.name + "': another installed entry uses it too"); return; }
			List<string> worlds = plan ? WorldsUsing(null, f.name) : WorldsUsing(f.name);
			if (worlds.Count > 0) { report.Add("Kept '" + f.name + "': " + (worlds.Count == 1 ? "the world " : "the worlds ") + string.Join(", ", worlds.Select(w => "'" + w + "'").ToArray()) + (worlds.Count == 1 ? " uses it" : " use it")); return; }
			File.Delete(path);
			if (!plan) RemovePoolLine(f.name);
			report.Add("Removed " + (plan ? "the plan '" : "'") + f.name + "'");
		}

		/// <summary>The copies of islands downloaded from multiplayer hosts (&lt;name&gt;_&lt;hash&gt;), with the saved worlds that use each.</summary>
		public static List<KeyValuePair<string, List<string>>> HostCopies()
		{
			var result = new List<KeyValuePair<string, List<string>>>();
			foreach (string n in IslandSpawner.ListSavedIslands().Where(IslandNetwork.IsDownloadName))
			{
				int i = n.LastIndexOf('_');
				string original = n.Substring(0, i), hash = n.Substring(i + 1);
				var worlds = new List<string>();
				try
				{
					if (Directory.Exists(WorldsFolder))
						foreach (string file in Directory.GetFiles(WorldsFolder, "*.txt"))
						{
							string[] lines = File.ReadAllLines(file);
							if (!lines.Any(l => { string[] p = l.Split('|'); return !l.StartsWith("@") && p.Length > 7 && p[0].Trim().Equals(original, StringComparison.OrdinalIgnoreCase) && p[7].Trim() == hash; })) continue;
							string head = lines.FirstOrDefault(l => l.StartsWith("# Custom islands in world '"));
							worlds.Add(head != null ? head.Substring(head.IndexOf('\'') + 1).Split('\'')[0] : Path.GetFileNameWithoutExtension(file));
						}
				}
				catch { }
				result.Add(new KeyValuePair<string, List<string>>(n, worlds));
			}
			return result;
		}

		/// <summary>Deletes the downloaded host copies no saved world uses. Returns how many.</summary>
		public static int RemoveUnusedHostCopies()
		{
			int n = 0;
			foreach (var c in HostCopies().Where(c => c.Value.Count == 0))
			{
				try { File.Delete(IslandSpawner.PathFor(c.Key)); n++; } catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not remove " + c.Key + ": " + e.Message); }
			}
			if (n > 0) { Log("Removed " + n + " unused island copies downloaded from hosts"); IslandCache.Forget(); }
			return n;
		}

		#endregion
	}

	/// <summary>Where a world's plan came from (WorldDirector.PlanFrom): an installed entry, or the player's own.</summary>
	public static class LibrarySource
	{
		/// <summary>"library:&lt;id&gt;@&lt;version&gt;" / "import:..." for a plan an entry installed, else null.</summary>
		public static string OfPlan(string planName)
		{
			LibraryInstalled e = LibraryPack.OwnerOf(planName + WorldPlan.Extension);
			return e != null ? e.Source : null;
		}

		/// <summary>An entry's title from a source string (or its id when it isn't installed here).</summary>
		public static string Describe(string source)
		{
			string rest = source.Substring(source.IndexOf(':') + 1);
			string id = rest.Split('@')[0];
			LibraryInstalled e = LibraryPack.Installed().FirstOrDefault(x => x.id.Equals(id, StringComparison.OrdinalIgnoreCase));
			return e != null ? e.title : id;
		}
	}
}
