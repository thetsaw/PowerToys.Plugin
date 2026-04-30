// Copyright (c) Thet. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using System.Windows;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Wox.Plugin.Logger;

namespace Community.PowerToys.Run.Plugin.DiskAnalyzer
{
    /// <summary>
    /// PowerToys Run plugin that provides TreeSize-like disk space analysis.
    /// Activation keyword: ds
    /// </summary>
    public class Main : IPlugin, IDelayedExecutionPlugin, IContextMenu, ISettingProvider, IDisposable
    {
        public static string PluginID => "B4F2E8A1C3D64F7E9A1B2C3D4E5F6A7B";

        public string Name => "DiskAnalyzer";

        public string Description => "Analyze disk space usage like TreeSize. Scan folders, find large files, and view drive info.";

        private PluginInitContext? _context;
        private string? _iconPath;
        private bool _disposed;

        // Settings
        private int _maxResults = 15;
        private int _maxDepth = 1;
        private bool _includeHiddenFiles = false;
        private bool _showPercentage = true;
        // Sort order reserved for future use

        public void Init(PluginInitContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _context.API.ThemeChanged += OnThemeChanged;
            UpdateIconPath(_context.API.GetCurrentTheme());
        }

        /// <summary>
        /// Fast initial results — shows command hints.
        /// </summary>
        public List<Result> Query(Query query)
        {
            var search = query.Search?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(search))
            {
                return GetHelpResults();
            }

            // "drives" command — fast, no delayed execution needed
            if (search.Equals("drives", StringComparison.OrdinalIgnoreCase))
            {
                return GetDriveResults();
            }

            return new List<Result>();
        }

        /// <summary>
        /// Delayed execution for expensive disk I/O operations.
        /// </summary>
        public List<Result> Query(Query query, bool delayedExecution)
        {
            if (!delayedExecution)
            {
                return new List<Result>();
            }

            var search = query.Search?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(search) ||
                search.Equals("drives", StringComparison.OrdinalIgnoreCase))
            {
                return new List<Result>();
            }

            try
            {
                // "largest <path>" — find largest files
                if (search.StartsWith("largest ", StringComparison.OrdinalIgnoreCase))
                {
                    var path = search[8..].Trim().Trim('"');
                    return GetLargestFilesResults(path);
                }

                // "top <path>" — top subdirectories by size
                if (search.StartsWith("top ", StringComparison.OrdinalIgnoreCase))
                {
                    var path = search[4..].Trim().Trim('"');
                    return GetTopFoldersResults(path);
                }

                // Direct path — scan the directory
                if (DiskAnalyzerHelper.IsValidPath(search))
                {
                    return GetDirectoryScanResults(search.Trim('"'));
                }

                // Partial path or search term
                return GetSuggestionResults(search);
            }
            catch (UnauthorizedAccessException)
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Access Denied",
                        SubTitle = $"Cannot access: {search}. Try running PowerToys as Administrator.",
                        IcoPath = _iconPath,
                        Score = 100,
                    },
                };
            }
            catch (Exception ex)
            {
                Log.Error($"DiskAnalyzer query error: {ex.Message}", GetType());
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Error analyzing path",
                        SubTitle = ex.Message,
                        IcoPath = _iconPath,
                        Score = 100,
                    },
                };
            }
        }

        /// <summary>
        /// Context menu actions for results.
        /// </summary>
        public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
        {
            var menus = new List<ContextMenuResult>();

            if (selectedResult?.ContextData is not DiskItemInfo item)
            {
                return menus;
            }

            // Open in File Explorer
            menus.Add(new ContextMenuResult
            {
                PluginName = Name,
                Title = "Open in File Explorer (Enter)",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Glyph = "\xE838", // FolderOpen
                AcceleratorKey = Key.Enter,
                Action = _ =>
                {
                    try
                    {
                        var path = item.FullPath;
                        if (item.IsFile)
                        {
                            // Select the file in Explorer
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                        }
                        else
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Failed to open path: {ex.Message}", GetType());
                        return false;
                    }
                },
            });

            // Copy path
            menus.Add(new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy path (Ctrl+C)",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Glyph = "\xE8C8", // Copy
                AcceleratorKey = Key.C,
                AcceleratorModifiers = ModifierKeys.Control,
                Action = _ =>
                {
                    Clipboard.SetText(item.FullPath);
                    return true;
                },
            });

            // Copy size
            menus.Add(new ContextMenuResult
            {
                PluginName = Name,
                Title = $"Copy size: {DiskAnalyzerHelper.FormatSize(item.SizeBytes)} (Ctrl+Shift+C)",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Glyph = "\xE8EF", // ReportDocument
                AcceleratorKey = Key.C,
                AcceleratorModifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Action = _ =>
                {
                    Clipboard.SetText(DiskAnalyzerHelper.FormatSize(item.SizeBytes));
                    return true;
                },
            });

            // Drill down (for folders)
            if (!item.IsFile)
            {
                menus.Add(new ContextMenuResult
                {
                    PluginName = Name,
                    Title = "Scan this folder (Ctrl+Enter)",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    Glyph = "\xE721", // Search
                    AcceleratorKey = Key.Enter,
                    AcceleratorModifiers = ModifierKeys.Control,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} {item.FullPath}");
                        return false;
                    },
                });

                // Find largest files in folder
                menus.Add(new ContextMenuResult
                {
                    PluginName = Name,
                    Title = "Find largest files here (Ctrl+L)",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    Glyph = "\xE74C", // SortLines
                    AcceleratorKey = Key.L,
                    AcceleratorModifiers = ModifierKeys.Control,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} largest {item.FullPath}");
                        return false;
                    },
                });
            }

            return menus;
        }

        #region Settings

        public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
        {
            new PluginAdditionalOption
            {
                Key = "MaxResults",
                DisplayLabel = "Maximum results",
                DisplayDescription = "Maximum number of items to display (5-50)",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
                NumberValue = _maxResults,
                NumberBoxMin = 5,
                NumberBoxMax = 50,
            },
            new PluginAdditionalOption
            {
                Key = "MaxDepth",
                DisplayLabel = "Default scan depth",
                DisplayDescription = "How many levels deep to scan by default (1-5)",
                PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Numberbox,
                NumberValue = _maxDepth,
                NumberBoxMin = 1,
                NumberBoxMax = 5,
            },
            new PluginAdditionalOption
            {
                Key = "IncludeHidden",
                DisplayLabel = "Include hidden files and folders",
                DisplayDescription = "Include items with the Hidden attribute in results",
                Value = _includeHiddenFiles,
            },
            new PluginAdditionalOption
            {
                Key = "ShowPercentage",
                DisplayLabel = "Show percentage of parent",
                DisplayDescription = "Display what percentage of the parent folder each item uses",
                Value = _showPercentage,
            },
        };

        public System.Windows.Controls.Control CreateSettingPanel()
        {
            throw new NotImplementedException();
        }

        public void UpdateSettings(PowerLauncherPluginSettings settings)
        {
            if (settings?.AdditionalOptions == null)
            {
                return;
            }

            foreach (var option in settings.AdditionalOptions)
            {
                switch (option.Key)
                {
                    case "MaxResults":
                        _maxResults = (int)option.NumberValue;
                        break;
                    case "MaxDepth":
                        _maxDepth = (int)option.NumberValue;
                        break;
                    case "IncludeHidden":
                        _includeHiddenFiles = option.Value;
                        break;
                    case "ShowPercentage":
                        _showPercentage = option.Value;
                        break;
                }
            }
        }

        #endregion

        #region Result Builders

        private List<Result> GetHelpResults()
        {
            return new List<Result>
            {
                new Result
                {
                    Title = "Disk Analyzer — TreeSize-like disk usage tool",
                    SubTitle = "Type a path, 'drives', 'largest <path>', or 'top <path>'",
                    IcoPath = _iconPath,
                    Score = 1000,
                },
                new Result
                {
                    Title = "ds drives",
                    SubTitle = "Show all drives with used/free/total space",
                    IcoPath = _iconPath,
                    Score = 900,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} drives");
                        return false;
                    },
                },
                new Result
                {
                    Title = "ds C:\\Users",
                    SubTitle = "Scan a folder — shows subfolders sorted by size",
                    IcoPath = _iconPath,
                    Score = 800,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} C:\\Users");
                        return false;
                    },
                },
                new Result
                {
                    Title = "ds largest C:\\",
                    SubTitle = "Find the largest files in a directory (recursive)",
                    IcoPath = _iconPath,
                    Score = 700,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} largest C:\\");
                        return false;
                    },
                },
                new Result
                {
                    Title = "ds top C:\\",
                    SubTitle = "Show top-level folders ranked by total size",
                    IcoPath = _iconPath,
                    Score = 600,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} top C:\\");
                        return false;
                    },
                },
            };
        }

        private List<Result> GetDriveResults()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .OrderByDescending(d => d.TotalSize - d.AvailableFreeSpace)
                .ToList();

            var results = new List<Result>();

            foreach (var drive in drives)
            {
                var usedBytes = drive.TotalSize - drive.AvailableFreeSpace;
                var usedPercent = (double)usedBytes / drive.TotalSize * 100;
                var bar = DiskAnalyzerHelper.CreateProgressBar(usedPercent);

                results.Add(new Result
                {
                    Title = $"{drive.Name} — {DiskAnalyzerHelper.FormatSize(usedBytes)} used of {DiskAnalyzerHelper.FormatSize(drive.TotalSize)}  ({usedPercent:F1}%)",
                    SubTitle = $"{bar}  Free: {DiskAnalyzerHelper.FormatSize(drive.AvailableFreeSpace)}  |  {drive.DriveFormat}  |  {drive.VolumeLabel}",
                    IcoPath = _iconPath,
                    Score = (int)usedPercent,
                    ToolTipData = new ToolTipData(
                        $"Drive {drive.Name}",
                        $"Label: {drive.VolumeLabel}\n" +
                        $"Type: {drive.DriveType}\n" +
                        $"Format: {drive.DriveFormat}\n" +
                        $"Total: {DiskAnalyzerHelper.FormatSize(drive.TotalSize)}\n" +
                        $"Used: {DiskAnalyzerHelper.FormatSize(usedBytes)} ({usedPercent:F1}%)\n" +
                        $"Free: {DiskAnalyzerHelper.FormatSize(drive.AvailableFreeSpace)}"),
                    ContextData = new DiskItemInfo
                    {
                        FullPath = drive.Name,
                        SizeBytes = usedBytes,
                        IsFile = false,
                    },
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} {drive.Name}");
                        return false;
                    },
                });
            }

            return results;
        }

        private List<Result> GetDirectoryScanResults(string path)
        {
            if (!Directory.Exists(path))
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Directory not found",
                        SubTitle = $"Path does not exist: {path}",
                        IcoPath = _iconPath,
                        Score = 100,
                    },
                };
            }

            var items = DiskAnalyzerHelper.ScanDirectory(path, _maxDepth, _includeHiddenFiles);
            var parentSize = items.Sum(i => i.SizeBytes);

            // Add parent folder summary as first result
            var results = new List<Result>
            {
                new Result
                {
                    Title = $"📁 {path} — Total: {DiskAnalyzerHelper.FormatSize(parentSize)}",
                    SubTitle = $"{items.Count(i => !i.IsFile)} folders, {items.Count(i => i.IsFile)} files scanned",
                    IcoPath = _iconPath,
                    Score = 10000,
                    ContextData = new DiskItemInfo
                    {
                        FullPath = path,
                        SizeBytes = parentSize,
                        IsFile = false,
                    },
                    Action = _ =>
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
                        return true;
                    },
                },
            };

            var sorted = items
                .OrderByDescending(i => i.SizeBytes)
                .Take(_maxResults);

            int rank = 1;
            foreach (var item in sorted)
            {
                var icon = item.IsFile ? "📄" : "📁";
                var pct = parentSize > 0 ? (double)item.SizeBytes / parentSize * 100 : 0;
                var pctText = _showPercentage ? $"  ({pct:F1}%)" : string.Empty;
                var bar = DiskAnalyzerHelper.CreateMiniBar(pct);

                results.Add(new Result
                {
                    Title = $"{icon} {item.Name} — {DiskAnalyzerHelper.FormatSize(item.SizeBytes)}{pctText}",
                    SubTitle = $"{bar}  {item.FullPath}",
                    IcoPath = _iconPath,
                    Score = 10000 - rank,
                    ContextData = item,
                    Action = _ =>
                    {
                        if (item.IsFile)
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
                        }
                        else
                        {
                            _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} {item.FullPath}");
                            return false;
                        }

                        return true;
                    },
                });
                rank++;
            }

            return results;
        }

        private List<Result> GetLargestFilesResults(string path)
        {
            if (!Directory.Exists(path))
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Directory not found",
                        SubTitle = $"Path does not exist: {path}",
                        IcoPath = _iconPath,
                        Score = 100,
                    },
                };
            }

            var files = DiskAnalyzerHelper.FindLargestFiles(path, _maxResults, _includeHiddenFiles);

            if (!files.Any())
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "No files found",
                        SubTitle = $"No accessible files in: {path}",
                        IcoPath = _iconPath,
                        Score = 100,
                    },
                };
            }

            var results = new List<Result>
            {
                new Result
                {
                    Title = $"🔍 Largest files in: {path}",
                    SubTitle = $"Showing top {Math.Min(files.Count, _maxResults)} files by size (recursive scan)",
                    IcoPath = _iconPath,
                    Score = 10000,
                },
            };

            int rank = 1;
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file.Name).ToUpperInvariant();
                if (string.IsNullOrEmpty(ext)) ext = "(no ext)";

                results.Add(new Result
                {
                    Title = $"#{rank}  {file.Name} — {DiskAnalyzerHelper.FormatSize(file.SizeBytes)}",
                    SubTitle = $"{ext}  |  {file.FullPath}",
                    IcoPath = _iconPath,
                    Score = 10000 - rank,
                    ContextData = file,
                    ToolTipData = new ToolTipData(
                        file.Name,
                        $"Size: {DiskAnalyzerHelper.FormatSize(file.SizeBytes)} ({file.SizeBytes:N0} bytes)\n" +
                        $"Path: {file.FullPath}\n" +
                        $"Extension: {ext}\n" +
                        $"Modified: {file.LastModified:g}"),
                    Action = _ =>
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
                        return true;
                    },
                });
                rank++;
            }

            return results;
        }

        private List<Result> GetTopFoldersResults(string path)
        {
            if (!Directory.Exists(path))
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Directory not found",
                        SubTitle = $"Path does not exist: {path}",
                        IcoPath = _iconPath,
                        Score = 100,
                    },
                };
            }

            var folders = DiskAnalyzerHelper.GetTopFolders(path, _maxResults, _includeHiddenFiles);
            var totalSize = folders.Sum(f => f.SizeBytes);

            var results = new List<Result>
            {
                new Result
                {
                    Title = $"📊 Top folders in: {path} — Total: {DiskAnalyzerHelper.FormatSize(totalSize)}",
                    SubTitle = $"{folders.Count} top-level subfolders scanned",
                    IcoPath = _iconPath,
                    Score = 10000,
                    ContextData = new DiskItemInfo
                    {
                        FullPath = path,
                        SizeBytes = totalSize,
                        IsFile = false,
                    },
                },
            };

            int rank = 1;
            foreach (var folder in folders)
            {
                var pct = totalSize > 0 ? (double)folder.SizeBytes / totalSize * 100 : 0;
                var bar = DiskAnalyzerHelper.CreateMiniBar(pct);

                results.Add(new Result
                {
                    Title = $"📁 {folder.Name} — {DiskAnalyzerHelper.FormatSize(folder.SizeBytes)}  ({pct:F1}%)",
                    SubTitle = $"{bar}  Items: {folder.ItemCount}  |  {folder.FullPath}",
                    IcoPath = _iconPath,
                    Score = 10000 - rank,
                    ContextData = folder,
                    Action = _ =>
                    {
                        _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} {folder.FullPath}");
                        return false;
                    },
                });
                rank++;
            }

            return results;
        }

        private List<Result> GetSuggestionResults(string search)
        {
            var results = new List<Result>();

            // Try to auto-complete paths
            try
            {
                var dir = Path.GetDirectoryName(search);
                var pattern = Path.GetFileName(search);

                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var matches = Directory.GetDirectories(dir, $"{pattern}*")
                        .Take(10)
                        .Select((d, i) => new Result
                        {
                            Title = $"📁 {Path.GetFileName(d)}",
                            SubTitle = d,
                            IcoPath = _iconPath,
                            Score = 1000 - i,
                            Action = _ =>
                            {
                                _context?.API.ChangeQuery($"{_context.CurrentPluginMetadata.ActionKeyword} {d}");
                                return false;
                            },
                        });

                    results.AddRange(matches);
                }
            }
            catch
            {
                // Ignore path parsing errors
            }

            if (!results.Any())
            {
                results.Add(new Result
                {
                    Title = $"No results for: {search}",
                    SubTitle = "Try a valid path like C:\\Users, or commands: drives, largest <path>, top <path>",
                    IcoPath = _iconPath,
                    Score = 100,
                });
            }

            return results;
        }

        #endregion

        #region Theme

        private void OnThemeChanged(Theme currentTheme, Theme newTheme)
        {
            UpdateIconPath(newTheme);
        }

        private void UpdateIconPath(Theme theme)
        {
            if (theme == Theme.Light || theme == Theme.HighContrastWhite)
            {
                _iconPath = "Images/diskanalyzer.light.png";
            }
            else
            {
                _iconPath = "Images/diskanalyzer.dark.png";
            }
        }

        #endregion

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                if (_context?.API != null)
                {
                    _context.API.ThemeChanged -= OnThemeChanged;
                }

                _disposed = true;
            }
        }
    }
}
