﻿using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.Json;


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