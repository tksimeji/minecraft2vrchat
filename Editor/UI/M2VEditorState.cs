using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace M2V.Editor
{
    public sealed class M2VEditorState
    {
        private static readonly DimensionOption[] s_fixedDimensions =
        {
            new DimensionOption { Id = World.World.OverworldId, Label = "Overworld" },
            new DimensionOption { Id = World.World.NetherId, Label = "Nether" },
            new DimensionOption { Id = World.World.EndId, Label = "End" }
        };

        public int MinX = -10;
        public int MinY = 60;
        public int MinZ = -10;
        public int MaxX = 10;
        public int MaxY = 90;
        public int MaxZ = 10;

        public readonly List<WorldEntry> WorldEntries = new List<WorldEntry>();
        public DirectoryInfo CurrentWorldPath;
        public DirectoryInfo SelectedWorldPath;

        public void SetDefaultRange()
        {
            MinX = -10;
            MinY = 60;
            MinZ = -10;
            MaxX = 10;
            MaxY = 90;
            MaxZ = 10;
        }

        public void SetRangeFromSpawn(Vector3Int center)
        {
            MinX = center.x - 10;
            MinY = center.y - 10;
            MinZ = center.z - 10;
            MaxX = center.x + 10;
            MaxY = center.y + 20;
            MaxZ = center.z + 10;
        }

        public void GetRange(out Vector3Int min, out Vector3Int max)
        {
            min = new Vector3Int(Mathf.Min(MinX, MaxX), Mathf.Min(MinY, MaxY), Mathf.Min(MinZ, MaxZ));
            max = new Vector3Int(Mathf.Max(MinX, MaxX), Mathf.Max(MinY, MaxY), Mathf.Max(MinZ, MaxZ));
        }

        public string GetSelectedPath()
        {
            return SelectedWorldPath?.FullName ?? string.Empty;
        }

        public void SetSelectedWorld(DirectoryInfo dir)
        {
            SelectedWorldPath = dir;
        }

        public void SetCurrentWorld(DirectoryInfo dir)
        {
            CurrentWorldPath = dir;
        }

        public World.World GetSelectedWorld()
        {
            if (SelectedWorldPath == null)
            {
                return null;
            }

            return World.World.Of(SelectedWorldPath);
        }

        public bool IsSelectedWorldValid()
        {
            return GetSelectedWorld() != null;
        }

        public bool IsSameAsCurrent(string path)
        {
            if (CurrentWorldPath == null)
            {
                return string.IsNullOrEmpty(path);
            }

            var left = NormalizePath(CurrentWorldPath.FullName);
            var right = NormalizePath(path);
            return string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase);
        }

        public void PopulateWorldEntries(string savesPath,
            System.Func<string, bool> isValidWorld,
            System.Func<string, WorldMeta> readMeta,
            System.Func<string, Texture2D> loadIcon)
        {
            WorldEntries.Clear();
            if (string.IsNullOrEmpty(savesPath) || !Directory.Exists(savesPath))
            {
                return;
            }

            foreach (var dir in Directory.GetDirectories(savesPath))
            {
                if (isValidWorld != null && !isValidWorld(dir))
                {
                    continue;
                }

                var meta = readMeta != null ? readMeta(dir) : WorldMeta.Empty;
                var icon = loadIcon != null ? loadIcon(dir) : null;
                var info = new DirectoryInfo(dir);
                WorldEntries.Add(new WorldEntry
                {
                    Path = info,
                    Name = meta.Name,
                    Icon = icon,
                    IsValid = true,
                    FolderName = info.Name,
                    LastPlayed = meta.LastPlayed,
                    GameMode = meta.GameMode,
                    Version = meta.Version
                });
            }
        }

        public bool TryFindWorldIndex(DirectoryInfo path, out int index)
        {
            index = -1;
            if (path == null)
            {
                return false;
            }

            var target = NormalizePath(path.FullName);
            for (var i = 0; i < WorldEntries.Count; i++)
            {
                var entryPath = WorldEntries[i].Path?.FullName;
                if (!string.IsNullOrEmpty(entryPath) &&
                    string.Equals(NormalizePath(entryPath), target, System.StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        public bool TrySelectWorld(DirectoryInfo path, out WorldEntry entry)
        {
            entry = null;
            if (!TryFindWorldIndex(path, out var index))
            {
                return false;
            }

            entry = WorldEntries[index];
            SetSelectedWorld(entry.Path);
            return true;
        }

        public bool EnsureDefaultSelection()
        {
            if (WorldEntries.Count == 0)
            {
                return false;
            }

            if (SelectedWorldPath == null)
            {
                SetSelectedWorld(WorldEntries[0].Path);
                return true;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : Path.GetFullPath(path);
        }

        public sealed class WorldEntry
        {
            public DirectoryInfo Path;
            public string Name;
            public Texture2D Icon;
            public bool IsValid;
            public string FolderName;
            public string LastPlayed;
            public string GameMode;
            public string Version;
        }

        public readonly struct WorldMeta
        {
            public readonly string Name;
            public readonly string LastPlayed;
            public readonly string GameMode;
            public readonly string Version;

            public WorldMeta(string name, string lastPlayed, string gameMode, string version)
            {
                Name = name;
                LastPlayed = lastPlayed;
                GameMode = gameMode;
                Version = version;
            }

            public static WorldMeta Empty => new WorldMeta(string.Empty, string.Empty, "Unknown", string.Empty);
        }

        public sealed class DimensionOption
        {
            public string Id;
            public string Label;
        }
    }
}
