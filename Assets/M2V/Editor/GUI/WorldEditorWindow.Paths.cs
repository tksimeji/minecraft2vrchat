#nullable enable

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DomainWorld = M2V.Editor.Minecraft.World.World;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private static string GetMinecraftVersionJarPath(string versionName)
        {
            if (string.IsNullOrEmpty(versionName))
            {
                return string.Empty;
            }

            var roots = new List<string>();
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                roots.Add(Path.Combine(home, "Library", "Application Support", "minecraft", "versions")); // macOS
                roots.Add(Path.Combine(home, ".minecraft", "versions")); // Linux
            }

            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                roots.Add(Path.Combine(appData, ".minecraft", "versions")); // Windows
            }

            foreach (var root in roots)
            {
                var jarPath = Path.Combine(root, versionName, $"{versionName}.jar");
                if (File.Exists(jarPath))
                {
                    return jarPath;
                }
            }

            return string.Empty;
        }
        private static bool IsValidWorldFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return false;
            }

            return DomainWorld.Wrap(new DirectoryInfo(path)) != null;
        }
        private static DomainWorld? ResolveWorld(DirectoryInfo? directory)
        {
            return directory == null ? null : DomainWorld.Wrap(directory);
        }
        private static string GetDefaultWorldsPath()
        {
            var root = GetMinecraftRootPath();
            return string.IsNullOrEmpty(root) ? string.Empty : Path.Combine(root, "saves");
        }
        private static string GetMinecraftRootPath()
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return string.Empty;
            }

            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
            {
                var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                return string.IsNullOrEmpty(appData)
                    ? string.Empty
                    : Path.Combine(appData, ".minecraft");
            }

            if (UnityEngine.Application.platform == RuntimePlatform.OSXEditor)
            {
                return Path.Combine(home, "Library", "Application Support", "minecraft");
            }

            return Path.Combine(home, ".minecraft");
        }
    }
}