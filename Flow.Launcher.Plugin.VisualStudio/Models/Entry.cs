namespace Flow.Launcher.Plugin.VisualStudio.Models
{
    //For Json deserialization. Represent a recent item entry in Visual Studio
    public class Entry 
    {
        public string Key { get; init; }
        public Value Value { get; init; }
    }
}
