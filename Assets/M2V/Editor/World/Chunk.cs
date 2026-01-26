#nullable enable
using System.Collections.Generic;
using fNbt;
using M2V.Editor.World.Block;
using UnityEngine;

namespace M2V.Editor.World
{
    public sealed class Chunk
    {
        private static List<Section> ReadSections(NbtCompound chunkNbt)
        {
            var sections = new List<Section>();

            if (chunkNbt["sections"] is not NbtList sectionsTag || sectionsTag.Count == 0)
            {
                return sections;
            }

            foreach (var sectionTag in sectionsTag)
            {
                if (sectionTag is NbtCompound compound)
                {
                    sections.Add(new Section(compound));
                }
            }

            return sections;
        }

        private readonly List<Section> _sections;

        public Chunk(NbtCompound chunkNbt)
        {
            _sections = ReadSections(chunkNbt);
        }

        public IReadOnlyList<Section> Sections => _sections;

        public sealed class Section
        {
            private static (List<BlockState>?, long[]?) ReadBlockStateData(NbtCompound sectionNbt)
            {
                if (sectionNbt["block_states"] is not NbtCompound blockStates)
                {
                    return (null, null);
                }

                var paletteTag = blockStates["palette"] as NbtList;
                if (paletteTag == null || paletteTag.Count == 0)
                {
                    return (null, null);
                }

                var palette = ReadBlockStatePalette(paletteTag);
                if (palette.Count == 0)
                {
                    return (null, null);
                }

                return (palette, (blockStates["data"] as NbtLongArray)?.Value);
            }

            private static (List<string>?, long[]?) ReadBiomeData(NbtCompound sectionNbt)
            {
                if (sectionNbt["biomes"] is not NbtCompound biomes)
                {
                    return (null, null);
                }

                var paletteTag = biomes["palette"] as NbtList;
                if (paletteTag == null || paletteTag.Count == 0)
                {
                    return (null, null);
                }

                var palette = new List<string>(paletteTag.Count);
                foreach (var entry in paletteTag)
                {
                    if (entry is NbtString str)
                    {
                        palette.Add(str.Value ?? string.Empty);
                    }
                }

                if (palette.Count == 0)
                {
                    return (null, null);
                }

                return (palette, (biomes["data"] as NbtLongArray)?.Value);
            }

            private static List<BlockState> ReadBlockStatePalette(NbtList paletteTag)
            {
                var list = new List<BlockState>(paletteTag.Count);
                foreach (var entry in paletteTag)
                {
                    if (entry is NbtCompound compound)
                    {
                        list.Add(BlockState.FromNbt(compound));
                    }
                }

                return list;
            }

            public int Y { get; }
            private readonly List<BlockState>? _blockStatePalette;
            private readonly long[]? _blockStateData;
            private readonly List<string>? _biomePalette;
            private readonly long[]? _biomeData;

            internal Section(NbtCompound sectionNbt)
            {
                Y = sectionNbt["Y"] is NbtByte yTag ? unchecked((sbyte)yTag.Value) : 0;
                (_blockStatePalette, _blockStateData) = ReadBlockStateData(sectionNbt);
                (_biomePalette, _biomeData) = ReadBiomeData(sectionNbt);
            }

            public int MinY => Y * 16;

            public int MaxY => (Y * 16) + 15;

            public IReadOnlyList<BlockState>? BlockStatePalette => _blockStatePalette;

            public long[]? BlockStateData => _blockStateData;

            public IReadOnlyList<string>? BiomePalette => _biomePalette;

            public long[]? BiomeData => _biomeData;

            public long CountSolidBlocksInRange(int chunkMinX, int chunkMinZ, Vector3Int min, Vector3Int max,
                System.Func<BlockState, bool> isAir)
            {
                if (_blockStatePalette == null || _blockStatePalette.Count == 0)
                {
                    return 0;
                }

                var bits = GetBitsForSection(_blockStatePalette.Count, _blockStateData);
                var sectionMinY = MinY;
                var sectionMaxY = MaxY;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    return 0;
                }

                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (xMin > xMax || yMin > yMax || zMin > zMax)
                {
                    return 0;
                }

                var isAirPalette = new bool[_blockStatePalette.Count];
                for (var i = 0; i < _blockStatePalette.Count; i++)
                {
                    isAirPalette[i] = isAir != null && isAir(_blockStatePalette[i]);
                }

                if (bits == 0)
                {
                    return isAirPalette[0]
                        ? 0
                        : (long)(xMax - xMin + 1) * (yMax - yMin + 1) * (zMax - zMin + 1);
                }

                if (_blockStateData == null || _blockStateData.Length == 0)
                {
                    return 0;
                }

                long count = 0;
                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var index = (localY << 8) | (localZ << 4) | localX;
                            var paletteIndex = GetBlockStateIndex(_blockStateData, index, bits);
                            if (paletteIndex >= 0 && paletteIndex < isAirPalette.Length && !isAirPalette[paletteIndex])
                            {
                                count++;
                            }
                        }
                    }
                }

                return count;
            }

            public void FillBlocks(int chunkMinX, int chunkMinZ, Vector3Int min, Vector3Int max, int[] blocks,
                int sizeX, int sizeY, int sizeZ, System.Func<BlockState, int> getBlockId,
                System.Func<BlockState, bool> isAir, out int invalidCount, out int totalCount)
            {
                invalidCount = 0;
                totalCount = 0;
                if (_blockStatePalette == null || _blockStatePalette.Count == 0)
                {
                    return;
                }

                var sectionMinY = MinY;
                var sectionMaxY = MaxY;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    return;
                }

                var paletteIds = new int[_blockStatePalette.Count];
                var isAirPalette = new bool[_blockStatePalette.Count];
                for (var i = 0; i < _blockStatePalette.Count; i++)
                {
                    var entry = _blockStatePalette[i];
                    isAirPalette[i] = isAir != null && isAir(entry);
                    paletteIds[i] = getBlockId != null ? getBlockId(entry) : 0;
                }

                var bits = GetBitsForSection(_blockStatePalette.Count, _blockStateData);
                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (bits == 0)
                {
                    if (!isAirPalette[0])
                    {
                        var id = paletteIds[0];
                        if (id > 0)
                        {
                            for (var y = yMin; y <= yMax; y++)
                            {
                                for (var z = zMin; z <= zMax; z++)
                                {
                                    for (var x = xMin; x <= xMax; x++)
                                    {
                                        var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                        blocks[outIndex] = id;
                                    }
                                }
                            }
                        }
                    }

                    return;
                }

                if (_blockStateData == null || _blockStateData.Length == 0)
                {
                    return;
                }

                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var index = (localY << 8) | (localZ << 4) | localX;
                            var paletteIndex = GetBlockStateIndex(_blockStateData, index, bits);
                            totalCount++;
                            if (paletteIndex >= 0 && paletteIndex < paletteIds.Length)
                            {
                                var id = paletteIds[paletteIndex];
                                if (id > 0 && !isAirPalette[paletteIndex])
                                {
                                    var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                    blocks[outIndex] = id;
                                }
                            }
                            else
                            {
                                invalidCount++;
                            }
                        }
                    }
                }
            }

            public void FillBiomes(int chunkMinX, int chunkMinZ, Vector3Int min, Vector3Int max, int[] biomes,
                int sizeX, int sizeY, int sizeZ, System.Func<string, int> getBiomeIndex, int fallbackIndex)
            {
                if (_biomePalette == null || _biomePalette.Count == 0 || biomes == null)
                {
                    return;
                }

                var sectionMinY = MinY;
                var sectionMaxY = MaxY;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    return;
                }

                var paletteIndices = new int[_biomePalette.Count];
                for (var i = 0; i < _biomePalette.Count; i++)
                {
                    paletteIndices[i] = getBiomeIndex != null ? getBiomeIndex(_biomePalette[i]) : fallbackIndex;
                }

                var bits = GetBitsForBiomeSection(_biomePalette.Count, _biomeData);
                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (bits == 0)
                {
                    var biomeIndex = paletteIndices[0];
                    for (var y = yMin; y <= yMax; y++)
                    {
                        for (var z = zMin; z <= zMax; z++)
                        {
                            for (var x = xMin; x <= xMax; x++)
                            {
                                var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                                if (outIndex >= 0 && outIndex < biomes.Length)
                                {
                                    biomes[outIndex] = biomeIndex;
                                }
                            }
                        }
                    }

                    return;
                }

                if (_biomeData == null || _biomeData.Length == 0)
                {
                    return;
                }

                for (var y = yMin; y <= yMax; y++)
                {
                    var localY = y - sectionMinY;
                    var biomeY = localY >> 2;
                    for (var z = zMin; z <= zMax; z++)
                    {
                        var localZ = z - chunkMinZ;
                        var biomeZ = localZ >> 2;
                        for (var x = xMin; x <= xMax; x++)
                        {
                            var localX = x - chunkMinX;
                            var biomeX = localX >> 2;
                            var biomeIndex = (biomeY << 4) | (biomeZ << 2) | biomeX;
                            var paletteIndex = GetPackedIndex(_biomeData, biomeIndex, bits);
                            var resolvedIndex = (paletteIndex >= 0 && paletteIndex < paletteIndices.Length)
                                ? paletteIndices[paletteIndex]
                                : fallbackIndex;
                            var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                            if (outIndex >= 0 && outIndex < biomes.Length)
                            {
                                biomes[outIndex] = resolvedIndex;
                            }
                        }
                    }
                }
            }

            private static int GetBitsForSection(int paletteSize, long[]? blockStates)
            {
                if (paletteSize <= 1)
                {
                    return 0;
                }

                var bits = 4;
                while ((1 << bits) < paletteSize)
                {
                    bits++;
                }

                var dataBits = blockStates?.Length > 0 ? (int)System.Math.Floor((double)(blockStates.Length * 64) / 4096) : 0;
                if (dataBits > bits)
                {
                    bits = dataBits;
                }

                return bits;
            }

            private static int GetBitsForBiomeSection(int paletteSize, long[]? data)
            {
                if (paletteSize <= 1)
                {
                    return 0;
                }

                var bits = 1;
                while ((1 << bits) < paletteSize)
                {
                    bits++;
                }

                if (data != null && data.Length > bits)
                {
                    bits = data.Length;
                }

                return bits;
            }

            private static int GetBlockStateIndex(long[] blockStates, int index, int bits)
            {
                if (bits <= 0 || blockStates == null || blockStates.Length == 0)
                {
                    return 0;
                }

                return GetPackedIndex(blockStates, index, bits);
            }

            private static int GetPackedIndex(long[] data, int index, int bits)
            {
                var valuesPerLong = 64 / bits;
                var longIndex = index / valuesPerLong;
                if (longIndex < 0 || longIndex >= data.Length)
                {
                    return 0;
                }

                var valueIndex = index % valuesPerLong;
                var shift = valueIndex * bits;
                var mask = (1L << bits) - 1L;
                return (int)((data[longIndex] >> shift) & mask);
            }
        }
    }
}
