// Copyright (c) Thet. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Text;
using Wox.Plugin.Logger;

namespace Community.PowerToys.Run.Plugin.DiskAnalyzer
{
    /// <summary>
    /// Helper methods for disk scanning, size formatting, and UI elements.
    /// </summary>
    public static class DiskAnalyzerHelper
    {
        private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>
        /// Formats byte count into a human-readable string (e.g. "1.23 GB").
        /// </summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0)
            {
                return "0 B";
            }

            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < SizeUnits.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:F0} {SizeUnits[unitIndex]}"
                : $"{size:F2} {SizeUnits[unitIndex]}";
        }

        /// <summary>
        /// Creates a text-based progress bar for drive usage display.
        /// </summary>
        public static string CreateProgressBar(double percent, int width = 20)
        {
            var filled = (int)Math.Round(percent / 100 * width);
            filled = Math.Clamp(filled, 0, width);

            var bar = new StringBuilder();
            bar.Append('[');
            bar.Append('█', filled);
            bar.Append('░', width - filled);
            bar.Append(']');

            return bar.ToString();
        }

        /// <summary>
        /// Creates a compact progress bar for folder size display.
        /// </summary>
        public static string CreateMiniBar(double percent, int width = 10)
        {
            var filled = (int)Math.Round(percent / 100 * width);
            filled = Math.Clamp(filled, 0, width);

            var bar = new StringBuilder();
            bar.Append('▓', filled);
            bar.Append('░', width - filled);

            return bar.ToString();
        }

        /// <summary>
        /// Validates whether a string is a valid file system path.
        /// </summary>
        public static bool IsValidPath(string path)
        {
            try
            {
                path = path.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                // Must start with drive letter or UNC path
                if (!(path.Length >= 2 && path[1] == ':') && !path.StartsWith(@"\\"))
                {
                    return false;
                }

                // Check if it's a valid rooted path
                return Path.IsPathRooted(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Scans a directory and returns items (files + folders) with their sizes.
        /// Uses parallel enumeration for performance.
        /// </summary>
        public static List<DiskItemInfo> ScanDirectory(string path, int maxDepth, bool includeHidden)
        {
            var items = new List<DiskItemInfo>();

            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists)
                {
                    return items;
                }

                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = includeHidden
                        ? FileAttributes.System
                        : FileAttributes.Hidden | FileAttributes.System,
                };

                // Get subdirectories with their total sizes
                try
                {
                    var subDirs = dirInfo.GetDirectories("*", options);
                    var folderItems = new DiskItemInfo[subDirs.Length];

                    Parallel.For(0, subDirs.Length, i =>
                    {
                        var sub = subDirs[i];
                        try
                        {
                            var (size, count) = CalculateDirectorySize(sub.FullName, maxDepth, includeHidden);
                            folderItems[i] = new DiskItemInfo
                            {
                                Name = sub.Name,
                                FullPath = sub.FullName,
                                SizeBytes = size,
                                IsFile = false,
                                ItemCount = count,
                                LastModified = sub.LastWriteTime,
                            };
                        }
                        catch
                        {
                            folderItems[i] = new DiskItemInfo
                            {
                                Name = sub.Name,
                                FullPath = sub.FullName,
                                SizeBytes = 0,
                                IsFile = false,
                                ItemCount = 0,
                                LastModified = sub.LastWriteTime,
                            };
                        }
                    });

                    items.AddRange(folderItems.Where(f => f != null));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Error enumerating directories in {path}: {ex.Message}", typeof(DiskAnalyzerHelper));
                }

                // Get files at this level
                try
                {
                    var files = dirInfo.GetFiles("*", options);
                    foreach (var file in files)
                    {
                        try
                        {
                            items.Add(new DiskItemInfo
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                SizeBytes = file.Length,
                                IsFile = true,
                                ItemCount = 1,
                                LastModified = file.LastWriteTime,
                            });
                        }
                        catch
                        {
                            // Skip files we can't read
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"Error enumerating files in {path}: {ex.Message}", typeof(DiskAnalyzerHelper));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error scanning directory {path}: {ex.Message}", typeof(DiskAnalyzerHelper));
            }

            return items;
        }

        /// <summary>
        /// Recursively finds the largest files under a given path.
        /// </summary>
        public static List<DiskItemInfo> FindLargestFiles(string path, int maxResults, bool includeHidden)
        {
            var files = new List<DiskItemInfo>();

            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = includeHidden
                        ? FileAttributes.System
                        : FileAttributes.Hidden | FileAttributes.System,
                };

                var dirInfo = new DirectoryInfo(path);
                var allFiles = dirInfo.EnumerateFiles("*", options);

                // Use a sorted set limited to maxResults for memory efficiency
                var topFiles = new SortedSet<(long Size, string Path, string Name, DateTime Modified)>(
                    Comparer<(long Size, string Path, string Name, DateTime Modified)>.Create(
                        (a, b) =>
                        {
                            var cmp = a.Size.CompareTo(b.Size);
                            return cmp != 0 ? cmp : string.Compare(a.Path, b.Path, StringComparison.Ordinal);
                        }));

                foreach (var file in allFiles)
                {
                    try
                    {
                        topFiles.Add((file.Length, file.FullName, file.Name, file.LastWriteTime));

                        if (topFiles.Count > maxResults)
                        {
                            topFiles.Remove(topFiles.Min);
                        }
                    }
                    catch
                    {
                        // Skip inaccessible files
                    }
                }

                files = topFiles
                    .OrderByDescending(f => f.Size)
                    .Select(f => new DiskItemInfo
                    {
                        Name = f.Name,
                        FullPath = f.Path,
                        SizeBytes = f.Size,
                        IsFile = true,
                        ItemCount = 1,
                        LastModified = f.Modified,
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"Error finding largest files in {path}: {ex.Message}", typeof(DiskAnalyzerHelper));
            }

            return files;
        }

        /// <summary>
        /// Gets top-level subdirectories ranked by total size.
        /// </summary>
        public static List<DiskItemInfo> GetTopFolders(string path, int maxResults, bool includeHidden)
        {
            var folders = new List<DiskItemInfo>();

            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists)
                {
                    return folders;
                }

                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    AttributesToSkip = includeHidden
                        ? FileAttributes.System
                        : FileAttributes.Hidden | FileAttributes.System,
                };

                var subDirs = dirInfo.GetDirectories("*", options);
                var folderItems = new DiskItemInfo[subDirs.Length];

                Parallel.For(0, subDirs.Length, i =>
                {
                    var sub = subDirs[i];
                    try
                    {
                        var (size, count) = CalculateDirectorySize(sub.FullName, depth: 10, includeHidden);
                        folderItems[i] = new DiskItemInfo
                        {
                            Name = sub.Name,
                            FullPath = sub.FullName,
                            SizeBytes = size,
                            IsFile = false,
                            ItemCount = count,
                            LastModified = sub.LastWriteTime,
                        };
                    }
                    catch
                    {
                        folderItems[i] = new DiskItemInfo
                        {
                            Name = sub.Name,
                            FullPath = sub.FullName,
                            SizeBytes = 0,
                            IsFile = false,
                            ItemCount = 0,
                            LastModified = sub.LastWriteTime,
                        };
                    }
                });

                folders = folderItems
                    .Where(f => f != null)
                    .OrderByDescending(f => f.SizeBytes)
                    .Take(maxResults)
                    .ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"Error getting top folders in {path}: {ex.Message}", typeof(DiskAnalyzerHelper));
            }

            return folders;
        }

        /// <summary>
        /// Calculates total size and item count for a directory tree.
        /// </summary>
        private static (long totalSize, int itemCount) CalculateDirectorySize(string path, int depth, bool includeHidden)
        {
            long totalSize = 0;
            int itemCount = 0;

            try
            {
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = depth > 1,
                    MaxRecursionDepth = depth,
                    AttributesToSkip = includeHidden
                        ? FileAttributes.System
                        : FileAttributes.Hidden | FileAttributes.System,
                };

                var dirInfo = new DirectoryInfo(path);
                foreach (var file in dirInfo.EnumerateFiles("*", options))
                {
                    try
                    {
                        totalSize += file.Length;
                        itemCount++;
                    }
                    catch
                    {
                        // Skip inaccessible files
                    }
                }
            }
            catch
            {
                // Return what we have so far
            }

            return (totalSize, itemCount);
        }
    }
}
