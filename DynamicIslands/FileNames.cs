using System;
using System.IO;
using System.Linq;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Names players type that become file names (islands, plans, groups, stamps, generator presets): what Windows can't
	/// store. Besides the characters it refuses, Windows keeps device names for itself (CON, NUL, COM1 ... - also with
	/// an extension: "con.island" can't be written), drops a trailing dot or space, and refuses a path longer than 260
	/// characters (Raft's folder is often long already).
	/// </summary>
	public static class FileNames
	{
		public const int MaxLength = 60;

		static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "CLOCK$",
			"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
			"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

		/// <summary>Why this name can't be a file name (for the player), or null when it can.</summary>
		public static string Problem(string name)
		{
			if (string.IsNullOrEmpty(name) || name.Trim().Length == 0) return "Type a name first.";
			if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A name can't contain \\ / : * ? \" < > |";
			if (name.Length > MaxLength) return "That name is too long (at most " + MaxLength + " characters).";
			if (name != name.Trim() || name.EndsWith(".")) return "A name can't begin or end with a space, or end with a dot.";
			if (IsReserved(name)) return "Windows keeps the name '" + name + "' for itself - choose another.";
			return null;
		}

		/// <summary>One of Windows' device names, with or without an extension ("con", "Nul.island", "com1.zip").</summary>
		public static bool IsReserved(string name)
		{
			return !string.IsNullOrEmpty(name) && Reserved.Contains(name.Split('.')[0].Trim().ToUpperInvariant());
		}

		public static bool Valid(string name) { return Problem(name) == null; }
	}

	/// <summary>
	/// Writes a file so that it is never left half written: to "&lt;file&gt;.tmp" first, then in its place. If Raft is closed or
	/// the disk is full in the middle, the file as it was is still there (the world's state, plans, what the library installed).
	/// </summary>
	public static class SafeFile
	{
		public static void WriteAllBytes(string path, byte[] bytes)
		{
			string tmp = path + ".tmp";
			File.WriteAllBytes(tmp, bytes);
			Commit(tmp, path);
		}

		/// <summary>
		/// A fully written "&lt;file&gt;.tmp" takes the file's place in one step (Windows' ReplaceFile): there is no moment
		/// with neither file. (Deleting first and then moving left only the .tmp when Raft stopped between the two.) Where
		/// replacing isn't possible (a file system without it), the old way, which Recover repairs.
		/// </summary>
		public static void Commit(string tmp, string path)
		{
			if (!File.Exists(path)) { File.Move(tmp, path); return; }
			try { File.Replace(tmp, path, null); return; }
			catch (Exception e) { UnityEngine.Debug.Log("[CUSTOM ISLANDS] Replacing " + Path.GetFileName(path) + " in one step didn't work (" + e.GetType().Name + "): deleting, then moving"); }
			if (File.Exists(path)) File.Delete(path);
			File.Move(tmp, path);
		}

		/// <summary>
		/// A write that stopped half way: the file is gone but its ".tmp" is there - that one was complete (the old file
		/// is only removed after the new one is fully written), so it becomes the file. A ".tmp" next to its file is an
		/// unfinished write and is removed. True when the file was brought back.
		/// </summary>
		public static bool Recover(string path)
		{
			string tmp = path + ".tmp";
			try
			{
				if (!File.Exists(tmp)) return false;
				if (File.Exists(path)) { File.Delete(tmp); return false; }
				File.Move(tmp, path);
				UnityEngine.Debug.LogWarning("[CUSTOM ISLANDS] " + Path.GetFileName(path) + " was being saved when Raft stopped: its new version is back");
				return true;
			}
			catch (Exception e) { UnityEngine.Debug.LogWarning("[CUSTOM ISLANDS] Could not recover " + path + ": " + e.Message); return false; }
		}

		/// <summary>Every ".tmp" under a folder (and its folders): brought back or removed. Once when the mod starts.</summary>
		public static int RecoverAll(string folder)
		{
			int n = 0;
			try
			{
				if (!Directory.Exists(folder)) return 0;
				foreach (string tmp in Directory.GetFiles(folder, "*.tmp", SearchOption.AllDirectories))
					if (Recover(tmp.Substring(0, tmp.Length - 4))) n++;
			}
			catch (Exception e) { UnityEngine.Debug.LogWarning("[CUSTOM ISLANDS] Looking for unfinished saves: " + e.Message); }
			return n;
		}

		public static void WriteAllText(string path, string text) { WriteAllBytes(path, new System.Text.UTF8Encoding(false).GetBytes(text)); }

		public static void WriteAllLines(string path, string[] lines) { WriteAllText(path, string.Join("\r\n", lines) + "\r\n"); }
	}
}
