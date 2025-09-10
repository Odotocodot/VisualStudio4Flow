using System;

namespace Flow.Launcher.Plugin.VisualStudio.Models
{
	public class Value
	{
		public LocalProperties LocalProperties { get; init; }
		public object Remote { get; init; }
		public bool IsFavorite { get; init; }
		public DateTime LastAccessed { get; init; }

		public bool IsLocal { get; init; }
		public bool HasRemote { get; init; }
		public bool IsSourceControlled { get; init; }
	}
}