#nullable enable
using System;
using System.IO;
using fNbt;
using UnityEngine;

namespace M2V.Editor.Minecraft.World
{
    public enum LevelStem
    {
        Overworld,
        Nether,
        End
    }

    public sealed class World
    {
        public static World? Wrap(DirectoryInfo root)
        {
            if (!root.Exists) return null;

            var levelDatPath = Path.Combine(root.FullName, "level.dat");
            if (!File.Exists(levelDatPath)) return null;

            try
            {
                var nbt = new NbtFile();
                nbt.LoadFromFile(levelDatPath);

                return nbt.RootTag["Data"] is NbtCompound dataTag
                    ? new World(root, dataTag)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            if (divisor == 0) return 0;
            if (value >= 0) return value / divisor;
            return -((-value + divisor - 1) / divisor);
        }

        public string LevelName => (_levelDatData["LevelName"] as NbtString)?.Value ?? _rootDir.Name;

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

        public Vector3Int? SpawnPos
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

        private readonly DirectoryInfo _rootDir;
        private readonly NbtCompound _levelDatData;

        private World(DirectoryInfo root, NbtCompound levelDatData)
        {
            _rootDir = root;
            _levelDatData = levelDatData;
        }

        public bool Has(LevelStem levelStem) => ResolveRegionDir(levelStem).Exists;

        public Chunk? GetChunkAt(LevelStem levelStem, int chunkX, int chunkZ)
        {
            var region = GetRegionAt(levelStem, chunkX, chunkZ);
            return region.GetChunkAt(chunkX, chunkZ);
        }

        private Region GetRegionAt(LevelStem levelStem, int chunkX, int chunkZ)
        {
            var regionFolder = ResolveRegionDir(levelStem);
            var regionX = FloorDiv(chunkX, 32);
            var regionZ = FloorDiv(chunkZ, 32);
            return new Region(regionFolder, regionX, regionZ);
        }

        private DirectoryInfo ResolveRegionDir(LevelStem levelStem)
        {
            var levelStemDir = levelStem switch
            {
                LevelStem.Overworld => _rootDir,
                LevelStem.Nether => new DirectoryInfo(Path.Combine(_rootDir.FullName, "DIM-1")),
                LevelStem.End => new DirectoryInfo(Path.Combine(_rootDir.FullName, "DIM1")),
                _ => throw new ArgumentOutOfRangeException(nameof(levelStem), levelStem, null)
            };
            return new DirectoryInfo(Path.Combine(levelStemDir.FullName, "region"));
        }
    }
}