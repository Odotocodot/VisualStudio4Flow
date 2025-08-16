namespace Flow.Launcher.Plugin.VisualStudio.Models
{
	public class LocalProperties
	{
		public string FullPath { get; init; }
		//0 is a solution/project.
		//1 is a file/folder
		public int Type { get; init; }
		public object SourceControl { get; init; }
	}
}