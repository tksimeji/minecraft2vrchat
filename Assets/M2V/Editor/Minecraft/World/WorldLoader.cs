using System.IO;

namespace M2V.Editor.Minecraft.World
{
    public static class WorldLoader
    {
        public static World LoadWorld(string worldFolder)
        {
            return string.IsNullOrEmpty(worldFolder) ? null : World.Wrap(new DirectoryInfo(worldFolder));
        }

        public static FileSystemInfo FindResourcePack(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder)) return null;

            var zipPath = Path.Combine(worldFolder, "resources.zip");
            if (File.Exists(zipPath))
            {
                return new FileInfo(zipPath);
            }

            var folderPath = Path.Combine(worldFolder, "resources");
            return Directory.Exists(folderPath) ? new DirectoryInfo(folderPath) : null;
        }

        public static FileSystemInfo FindDataPack(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return null;
            }

            var folderPath = Path.Combine(worldFolder, "datapacks");
            if (Directory.Exists(folderPath))
            {
                return new DirectoryInfo(folderPath);
            }

            return null;
        }
    }
}