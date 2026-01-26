#nullable enable
using System;
using System.IO;
using fNbt;

namespace M2V.Editor.World
{
    public sealed class Region
    {
        private const int RegionSize = 32;
        private const int SectorBytes = 4096;
        private const int HeaderBytes = SectorBytes * 2;

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

        private static bool TryGetLocalCoords(int chunkX, int chunkZ, int regionX, int regionZ, out int localX, out int localZ)
        {
            localX = chunkX - regionX * RegionSize;
            localZ = chunkZ - regionZ * RegionSize;
            return localX >= 0 && localX < RegionSize && localZ >= 0 && localZ < RegionSize;
        }

        private static int ReadInt32BE(Stream stream)
        {
            var b0 = stream.ReadByte();
            var b1 = stream.ReadByte();
            var b2 = stream.ReadByte();
            var b3 = stream.ReadByte();
            if (b0 < 0 || b1 < 0 || b2 < 0 || b3 < 0)
            {
                return 0;
            }

            return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
        }

        private static int ReadUInt24At(Stream stream, int offset)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var b0 = stream.ReadByte();
            var b1 = stream.ReadByte();
            var b2 = stream.ReadByte();
            if (b0 < 0 || b1 < 0 || b2 < 0)
            {
                return 0;
            }

            return (b0 << 16) | (b1 << 8) | b2;
        }

        private static byte ReadByteAt(Stream stream, int offset)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var value = stream.ReadByte();
            return value < 0 ? (byte)0 : (byte)value;
        }

        private readonly string _regionFolder;
        private readonly string _filePath;

        public string RegionFolder => _regionFolder;
        public int RegionX { get; }
        public int RegionZ { get; }
        public string Path => _filePath;

        internal Region(string regionFolder, int regionX, int regionZ)
        {
            _regionFolder = regionFolder ?? string.Empty;
            RegionX = regionX;
            RegionZ = regionZ;
            _filePath = string.IsNullOrEmpty(_regionFolder)
                ? string.Empty
                : System.IO.Path.Combine(_regionFolder, $"r.{RegionX}.{RegionZ}.mca");
        }

        public bool Exists => !string.IsNullOrEmpty(_filePath) && File.Exists(_filePath);

        public Chunk? GetChunkAt(int chunkX, int chunkZ)
        {
            if (!Exists)
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length < HeaderBytes)
                {
                    return null;
                }

                if (!TryGetLocalCoords(chunkX, chunkZ, RegionX, RegionZ, out var localX, out var localZ))
                {
                    return null;
                }

                var locationOffset = (localX + localZ * RegionSize) * 4;
                var sectorOffset = ReadUInt24At(stream, locationOffset);
                var sectorCount = ReadByteAt(stream, locationOffset + 3);
                if (sectorOffset == 0 || sectorCount == 0)
                {
                    return null;
                }

                var chunkDataOffset = sectorOffset * (long)SectorBytes;
                if (chunkDataOffset + 5 > stream.Length)
                {
                    return null;
                }

                stream.Seek(chunkDataOffset, SeekOrigin.Begin);
                var length = ReadInt32BE(stream);
                if (length <= 1)
                {
                    return null;
                }

                var compressionType = stream.ReadByte();
                if (compressionType == -1)
                {
                    return null;
                }

                var remaining = stream.Length - stream.Position;
                if (length - 1 > remaining)
                {
                    return null;
                }

                var compressed = new byte[length - 1];
                if (stream.Read(compressed, 0, compressed.Length) != compressed.Length)
                {
                    return null;
                }

                using var nbtStream = new MemoryStream(compressed);

                var compression = compressionType switch
                {
                    1 => NbtCompression.GZip,
                    2 => NbtCompression.ZLib,
                    3 => NbtCompression.None,
                    _ => throw new InvalidDataException($"Unknown compression type: {compressionType}")
                };

                var nbtFile = new NbtFile();
                nbtFile.LoadFromStream(nbtStream, compression);
                return nbtFile.RootTag != null ? new Chunk(nbtFile.RootTag) : null;
            }
            catch
            {
                return null;
            }
        }

    }
}
