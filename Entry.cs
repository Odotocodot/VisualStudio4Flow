using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;


namespace Flow.Launcher.Plugin.VisualStudio
{
    //For Json deserialization.

    public class Entry
    {
        public string Key { get; init; }
        [JsonPropertyName("Value")]
        public Properties Properties { get; init; }
    }

    public class Properties
    {
        public LocalProperties LocalProperties { get; init; }
        public object Remote { get; init; }
        public bool IsFavorite { get; init; }
        public DateTime LastAccessed { get; init; }

        public bool IsLocal { get; init; }
        public bool HasRemote { get; init; }
        public bool IsSourceControlled { get; init; }
    }

    public class LocalProperties
    {
        public string FullPath { get; init; }
        //0 is a solution/project.
        //1 is a file
        public int Type { get; init; }
        public object SourceControl { get; init; }
    }
}
