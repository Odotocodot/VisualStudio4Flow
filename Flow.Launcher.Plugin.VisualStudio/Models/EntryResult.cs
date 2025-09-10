using System;

namespace Flow.Launcher.Plugin.VisualStudio.Models
{
	// Wrapper around Entry
	public class EntryResult
	{
		public const string NoGit = "NoGit";

		public required Entry Entry { get; set; }

		public string Id => Entry.Key;
		public string Path => Entry.Value.LocalProperties.FullPath;
		public EntryType EntryType => Entry.Value.LocalProperties.Type switch
		{
			0 => EntryType.ProjectOrSolution,
			1 => EntryType.FileOrFolder,
			_ => EntryType.Unknown,
		};
		public DateTime LastAccessed => Entry.Value.LastAccessed;
		public bool IsFavorite => Entry.Value.IsFavorite;

		public string GitBranch { get; set; } = NoGit;
		public bool HasGit => GitBranch != NoGit;
		
	}
	
	public enum EntryType
	{
		ProjectOrSolution = 0,
		FileOrFolder = 1,
		Unknown = int.MaxValue,
	}

}