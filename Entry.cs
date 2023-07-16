using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Flow.Launcher.Plugin.VisualStudio
{
    //For Json deserialization.
    public class Entry 
    {
        public string Key { get; init; }
        public Value Value { get; init; }

        //NonJson
        [JsonIgnore]
        public string Path => Value.LocalProperties.FullPath;
        [JsonIgnore]
        public int ItemType => Value.LocalProperties.Type;
        [JsonIgnore]
        public List<int> HighlightData { get; set; }

        public override bool Equals(object obj)
        {
            return obj is Entry entry &&
                   Key == entry.Key;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Key);
        }
    }

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

    public class LocalProperties
    {
        public string FullPath { get; init; }
        //0 is a solution/project.
        //1 is a file
        public int Type { get; init; }
        public object SourceControl { get; init; }
    }
}
