#nullable enable

using System;
using System.IO;
using fNbt;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Minecraft.World;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private void RegisterDragHandlers()
        {
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.paths.Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.StopPropagation();
                }
            });

            rootVisualElement.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (DragAndDrop.paths.Length == 0)
                {
                    return;
                }

                var path = DragAndDrop.paths[0];
                if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    if (!SelectWorldInList(dirInfo))
                    {
                        _worldList.ClearSelection();
                        _state.SetSelectedWorld(dirInfo);
                    }
                    UpdateValidation();
                }

                evt.StopPropagation();
            });
        }
        private void ConfigureWorldList()
        {
            _worldList.selectionType = SelectionType.Single;
            _worldList.itemsSource = _state.WorldEntries;
            _worldList.fixedItemHeight = 84f;
            _worldList.makeItem = () =>
            {
                var root = new VisualElement();
                root.AddToClassList("m2v-world-item");

                var icon = new Image();
                icon.AddToClassList("m2v-world-icon");
                root.Add(icon);

                var info = new VisualElement();
                info.AddToClassList("m2v-world-info");

                var name = new Label();
                name.AddToClassList("m2v-world-name");
                info.Add(name);

                var path = new Label();
                path.AddToClassList("m2v-world-path");
                info.Add(path);

                var meta = new Label();
                meta.AddToClassList("m2v-world-meta");
                info.Add(meta);

                root.Add(info);
                return root;
            };

            _worldList.bindItem = (element, index) =>
            {
                var entry = _state.WorldEntries[index];
                var icon = element.Q<Image>();
                var name = element.Q<Label>(className: "m2v-world-name");
                var path = element.Q<Label>(className: "m2v-world-path");
                var meta = element.Q<Label>(className: "m2v-world-meta");

                icon.image = entry.Icon;
                name.text = string.IsNullOrEmpty(entry.Name) ? entry.FolderName : entry.Name;
                path.text = string.IsNullOrEmpty(entry.LastPlayed)
                    ? entry.FolderName
                    : $"{entry.FolderName} ({entry.LastPlayed})";
                meta.text = string.IsNullOrEmpty(entry.Version)
                    ? $"{entry.GameMode} {Localization.Get(_state.Language, Localization.Keys.ModeSuffix)}"
                    : $"{entry.GameMode} {Localization.Get(_state.Language, Localization.Keys.ModeSuffix)}, {Localization.Get(_state.Language, Localization.Keys.VersionLabel)}: {entry.Version}";
                element.tooltip = entry.Path?.FullName ?? string.Empty;

                element.EnableInClassList("selected", _worldList.selectedIndex == index);
            };

            _worldList.onSelectionChange += selection =>
            {
                foreach (var item in selection)
                {
                    if (item is EditorState.WorldEntry entry)
                    {
                        _state.SetSelectedWorld(entry.Path);
                        ApplySpawnDefaultRange(ResolveWorld(entry.Path));
                        UpdateValidation();
                        break;
                    }
                }

                _worldList.Rebuild();
            };
        }
        private void RefreshWorldList()
        {
            var savesPath = GetDefaultWorldsPath();
            _state.PopulateWorldEntries(savesPath, IsValidWorldFolder, TryGetWorldMeta, LoadWorldIcon);

            _worldList.Rebuild();
        }
        private bool SelectWorldInList(DirectoryInfo path)
        {
            if (_state.TryFindWorldIndex(path, out var index))
            {
                _worldList.SetSelection(index);
                return true;
            }

            return false;
        }
        private void UpdateValidation()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var path = _state.GetSelectedPath();
            var worldDir = _state.GetSelectedWorld();
            var isValid = worldDir != null;
            _openButton?.SetEnabled(!string.IsNullOrEmpty(path) && Directory.Exists(path));
            _nextWorldButton?.SetEnabled(isValid);
            _meshButton?.SetEnabled(isValid);

            _statusLabel.RemoveFromClassList("ok");
            _statusLabel.RemoveFromClassList("error");

            if (string.IsNullOrEmpty(path))
            {
                SetStatus(Localization.Get(_state.Language, Localization.Keys.StatusNoFolder), isOk: false);
                HandleWorldSelectionChanged(null, string.Empty);
                UpdateSummary();
                return;
            }

            if (isValid)
            {
                SetStatus(Localization.Get(_state.Language, Localization.Keys.StatusValid), isOk: true);
                HandleWorldSelectionChanged(worldDir, path);
            }
            else
            {
                SetStatus(Localization.Get(_state.Language, Localization.Keys.StatusInvalid), isOk: false);
                HandleWorldSelectionChanged(null, string.Empty);
            }
            UpdateSummary();
        }
        private void SetStatus(string text, bool isOk)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.text = text;
            _statusLabel.RemoveFromClassList("ok");
            _statusLabel.RemoveFromClassList("error");
            _statusLabel.AddToClassList(isOk ? "ok" : "error");
        }
        private void HandleWorldSelectionChanged(World? worldDir, string path)
        {
            if (!_state.IsSameAsCurrent(path))
            {
                _state.SetCurrentWorld(string.IsNullOrEmpty(path) ? null : new DirectoryInfo(path));
                UpdateDimensionChoices(path);
                ApplySpawnDefaultRange(worldDir);
            }

            if (worldDir == null)
            {
                UpdateDimensionChoices(string.Empty);
                _state.SetCurrentWorld(null);
            }
        }
        private void UpdateSummary()
        {
            var lang = _state.Language;
            var worldName = GetSelectedWorldLabel();
            _summaryWorld.text = $"{Localization.Get(lang, Localization.Keys.SummaryWorld)}: {worldName}";

            _state.GetRange(out var min, out var max);
            _summaryRange.text = $"{Localization.Get(lang, Localization.Keys.SummaryRange)}: ({min.x},{min.y},{min.z}) → ({max.x},{max.y},{max.z})";

            var dimensionLabel = GetSelectedDimensionLabel(lang);
            _summaryDimension.text = $"{Localization.Get(lang, Localization.Keys.SummaryDimension)}: {dimensionLabel}";

            _summaryScale.text = $"{Localization.Get(lang, Localization.Keys.SummaryScale)}: {_state.BlockScale:0.###}";

            var packsLabel = GetPacksSummary(lang);
            _summaryPacks.text = $"{Localization.Get(lang, Localization.Keys.SummaryPacks)}: {packsLabel}";
        }
        private string GetSelectedWorldLabel()
        {
            var path = _state.SelectedWorldPath;
            if (path == null)
            {
                return "-";
            }

            foreach (var entry in _state.WorldEntries)
            {
                if (entry?.Path == null)
                {
                    continue;
                }

                if (string.Equals(Path.GetFullPath(entry.Path.FullName), Path.GetFullPath(path.FullName), StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrEmpty(entry.Name) ? entry.FolderName : entry.Name;
                }
            }

            return path.Name;
        }
        private string GetPacksSummary(Language language)
        {
            var hasResource = ResolveWorldResourcePack(_state.SelectedWorldPath) != null;
            var hasData = ResolveWorldDataPack(_state.SelectedWorldPath) != null;
            if (!hasResource && !hasData)
            {
                return Localization.Get(language, Localization.Keys.SummaryNone);
            }

            if (hasResource && hasData)
            {
                return Localization.Get(language, Localization.Keys.SummaryResourceData);
            }

            return hasResource
                ? Localization.Get(language, Localization.Keys.SummaryResource)
                : Localization.Get(language, Localization.Keys.SummaryData);
        }
        private static FileSystemInfo? ResolveWorldResourcePack(DirectoryInfo? worldDir)
        {
            if (worldDir == null || !worldDir.Exists)
            {
                return null;
            }

            var zipPath = Path.Combine(worldDir.FullName, "resources.zip");
            if (File.Exists(zipPath))
            {
                return new FileInfo(zipPath);
            }

            var folderPath = Path.Combine(worldDir.FullName, "resources");
            if (Directory.Exists(folderPath))
            {
                return new DirectoryInfo(folderPath);
            }

            return null;
        }
        private static FileSystemInfo? ResolveWorldDataPack(DirectoryInfo? worldDir)
        {
            if (worldDir == null || !worldDir.Exists)
            {
                return null;
            }

            var folderPath = Path.Combine(worldDir.FullName, "datapacks");
            if (Directory.Exists(folderPath))
            {
                return new DirectoryInfo(folderPath);
            }

            return null;
        }
        private static Texture2D? LoadWorldIcon(string worldFolder)
        {
            var iconPath = Path.Combine(worldFolder, "icon.png");
            if (!File.Exists(iconPath))
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    Object.DestroyImmediate(texture);
                    return null;
                }

                texture.name = "WorldIcon";
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch
            {
                return null;
            }
        }
        private static string TryGetWorldName(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return string.Empty;
            }

            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                return string.Empty;
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                var root = nbtFile.RootTag;
                if (root == null)
                {
                    return string.Empty;
                }

                var dataTag = root["Data"] as NbtCompound;
                if (dataTag == null)
                {
                    return string.Empty;
                }

                var nameTag = dataTag["LevelName"] as NbtString;
                return nameTag != null ? nameTag.Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        private static EditorState.WorldMeta TryGetWorldMeta(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return EditorState.WorldMeta.Empty;
            }

            var worldDir = ResolveWorld(string.IsNullOrEmpty(worldFolder) ? null : new DirectoryInfo(worldFolder));
            if (worldDir == null)
            {
                return EditorState.WorldMeta.Empty;
            }

            return new EditorState.WorldMeta(
                worldDir.LevelName,
                FormatLastPlayed(worldDir.LastPlayedTime),
                FormatGameMode(worldDir.GameType),
                worldDir.VersionName);
        }
        private static string FormatLastPlayed(long value)
        {
            if (value <= 0)
            {
                return string.Empty;
            }

            try
            {
                DateTimeOffset time;
                if (value > 1_000_000_000_000)
                {
                    time = DateTimeOffset.FromUnixTimeMilliseconds(value);
                }
                else if (value > 1_000_000_000)
                {
                    time = DateTimeOffset.FromUnixTimeSeconds(value);
                }
                else
                {
                    return string.Empty;
                }

                return time.LocalDateTime.ToString("M/d/yy, h:mm tt");
            }
            catch
            {
                return string.Empty;
            }
        }
        private static string FormatGameMode(int gameType)
        {
            return gameType switch
            {
                0 => "Survival",
                1 => "Creative",
                2 => "Adventure",
                3 => "Spectator",
                _ => "Unknown"
            };
        }
    }
}
