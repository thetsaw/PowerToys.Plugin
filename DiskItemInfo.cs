// Copyright (c) Thet. All rights reserved.
// Licensed under the MIT License.

namespace Community.PowerToys.Run.Plugin.DiskAnalyzer
{
    /// <summary>
    /// Represents a file or folder with its size information.
    /// Used as ContextData for Result objects.
    /// </summary>
    public class DiskItemInfo
    {
        public string Name { get; set; } = string.Empty;

        public string FullPath { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public bool IsFile { get; set; }

        public int ItemCount { get; set; }

        public DateTime LastModified { get; set; }
    }
}
