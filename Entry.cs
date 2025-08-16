using System;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.VisualStudio
{
    //For Json deserialization. Represent a recent item entry in visual studio
    public class Entry 
    {
        public string Key { get; init; }
        public Value Value { get; init; }

        //Non Json
        //Move to own type EntryResult, keep this as a pure DTO 
        [JsonIgnore]
        public string GitBranch { get; set; }
        [JsonIgnore]
        public bool HasGit => GitBranch != null;
        [JsonIgnore]
        public string Path => Value.LocalProperties.FullPath;
        [JsonIgnore]
        public int ItemType => Value.LocalProperties.Type;

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
