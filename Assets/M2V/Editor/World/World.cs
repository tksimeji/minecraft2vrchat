#nullable enable
using System.IO;
using fNbt;
using UnityEngine;

namespace M2V.Editor.World
{
    public sealed class World
    {
        public const string OverworldId = "minecraft:overworld";
        public const string NetherId = "minecraft:the_nether";
        public const string EndId = "minecraft:the_end";
       
        public static World? Of(DirectoryInfo root)
        {
            if (!root.Exists) return null;
            
            var levelDatPath = Path.Combine(root.FullName, "level.dat");
            if (!File.Exists(levelDatPath)) return null;
            
            try
            {
                var nbt = new NbtFile();
                nbt.LoadFromFile(levelDatPath);
                
                return nbt.RootTag?["Data"] is NbtCompound dataTag
                    ? new World(root, dataTag)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDimensionFolderPath(string worldFolder, string dimensionId)
        {
            if (dimensionId == OverworldId)
            {
                return worldFolder;
            }

            if (dimensionId == NetherId)
            {
                return Path.Combine(worldFolder, "DIM-1");
            }

            if (dimensionId == EndId)
            {
                return Path.Combine(worldFolder, "DIM1");
            }

            if (dimensionId.Length == 0 || !dimensionId.Contains(":"))
            {
                return string.Empty;
            }

            var parts = dimensionId.Split(':');
            return Path.Combine(worldFolder, "dimensions", parts[0], parts[1]);
        }

        private static int FloorDiv(int value, int divisor)
        {
            if (divisor == 0)
            {
                return 0;
            }

            if (value >= 0)
            {
                return value / divisor;
            }

            return -(((-value) + divisor - 1) / divisor);
        }
        
        public string LevelName => (_levelDatData["LevelName"] as NbtString)?.Value ?? string.Empty;

        public long LastPlayedTime => (_levelDatData["LastPlayed"] as NbtLong)?.Value ?? 0;

        public int GameType => (_levelDatData["GameType"] as NbtInt)?.Value ?? 0;

        public string VersionName
        {
            get
            {
                if (_levelDatData["Version"] is not NbtCompound versionTag)
                {
                    return string.Empty;
                }

                if (versionTag["Name"] is NbtString nameTag && !string.IsNullOrEmpty(nameTag.Value))
                {
                    return nameTag.Value;
                }

                return string.Empty;
            }
        }

        private readonly DirectoryInfo _rootDir;
        private readonly NbtCompound _levelDatData;

        private World(DirectoryInfo root, NbtCompound levelDatData)
        {
            _rootDir = root;
            _levelDatData = levelDatData;
        }

        public Vector3Int? SpawnPosition
        {
            get
            {
                var spawnTag = _levelDatData["spawn"] as NbtCompound;
                var posTag = spawnTag?["pos"] as NbtIntArray;
                return posTag?.Value.Length == 3
                    ? new Vector3Int(posTag.Value[0], posTag.Value[1], posTag.Value[2])
                    : null;
            }
        }

        public bool HasRegionData(string dimensionId)
        {
            var regionFolder = GetRegionFolderPath(dimensionId);
            return !string.IsNullOrEmpty(regionFolder) && Directory.Exists(regionFolder);
        }

        public Region? GetRegionAt(string dimensionId, int chunkX, int chunkZ)
        {
            var regionFolder = GetRegionFolderPath(dimensionId);
            if (string.IsNullOrEmpty(regionFolder))
            {
                return null;
            }

            var regionX = FloorDiv(chunkX, 32);
            var regionZ = FloorDiv(chunkZ, 32);
            return new Region(regionFolder, regionX, regionZ);
        }

        public Chunk? GetOverworldChunkAt(int chunkX, int chunkZ)
        {
            var region = GetRegionAt(OverworldId, chunkX, chunkZ);
            return region?.GetChunkAt(chunkX, chunkZ);
        }

        public Chunk? GetNetherChunkAt(int chunkX, int chunkZ)
        {
            var region = GetRegionAt(NetherId, chunkX, chunkZ);
            return region?.GetChunkAt(chunkX, chunkZ);
        }

        public Chunk? GetEndChunkAt(int chunkX, int chunkZ)
        {
            var region = GetRegionAt(EndId, chunkX, chunkZ);
            return region?.GetChunkAt(chunkX, chunkZ);
        }

        public Chunk? GetChunkAt(string dimensionId, int chunkX, int chunkZ)
        {
            var region = GetRegionAt(dimensionId, chunkX, chunkZ);
            return region?.GetChunkAt(chunkX, chunkZ);
        }

        private string GetRegionFolderPath(string dimensionId)
        {
            var dimensionFolder = GetDimensionFolderPath(_rootDir.FullName, dimensionId);
            if (string.IsNullOrEmpty(dimensionFolder))
            {
                return string.Empty;
            }

            return Path.Combine(dimensionFolder, "region");
        }
    }
}
