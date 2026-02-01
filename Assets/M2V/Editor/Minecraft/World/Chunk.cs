#nullable enable
using System.Collections.Generic;
using fNbt;
using M2V.Editor.Minecraft;
using UnityEngine;

namespace M2V.Editor.Minecraft.World
{
    public sealed class Chunk
    {
        public IReadOnlyList<Section> Sections { get; }

        internal Chunk(NbtCompound nbt)
        {
            var sections = new List<Section>();
            if (nbt["sections"] is NbtList { Count: > 0 } sectionsTag)
            {
                foreach (var sectionTag in sectionsTag)
                {
                    if (sectionTag is NbtCompound compound)
                    {
                        sections.Add(new Section(compound));
                    }
                }
            }

            Sections = sections;
        }

        public sealed class Section
        {
            private static (List<BlockState>?, long[]?) ReadBlockStates(NbtCompound nbt)
            {
                if (nbt["block_states"] is not NbtCompound blockStates ||
                    blockStates["palette"] is not NbtList paletteTag || paletteTag.Count == 0)
                {
                    return (null, null);
                }

                var palette = ReadBlockPalette(paletteTag);
                return palette.Count == 0 ? (null, null) : (palette, (blockStates["data"] as NbtLongArray)?.Value);
            }

            private static (List<ResourceLocation>?, long[]?) ReadBiomes(NbtCompound nbt)
            {
                if (nbt["biomes"] is not NbtCompound biomes || biomes["palette"] is not NbtList paletteTag ||
                    paletteTag.Count == 0)
                {
                    return (null, null);
                }

                var palette = new List<ResourceLocation>(paletteTag.Count);
                foreach (var entry in paletteTag)
                {
                    if (entry is NbtString str)
                    {
                        var value = str.Value;
                        palette.Add(string.IsNullOrEmpty(value) ? ResourceLocation.Empty : ResourceLocation.Parse(value));
                    }
                }

                return palette.Count != 0 ? (palette, (biomes["data"] as NbtLongArray)?.Value) : (null, null);
            }

            private static List<BlockState> ReadBlockPalette(NbtList paletteTag)
            {
                var list = new List<BlockState>(paletteTag.Count);
                foreach (var entry in paletteTag)
                {
                    if (entry is NbtCompound compound)
                    {
                        list.Add(BlockState.From(compound));
                    }
                }

                return list;
            }

            public int SectionY { get; }

            public IReadOnlyList<BlockState>? BlockStatePalette => _blockStatePalette;

            private int MinY => SectionY * 16;
            private int MaxY => SectionY * 16 + 15;

            private readonly List<BlockState>? _blockStatePalette;
            private readonly long[]? _blockStateData;

            private readonly List<ResourceLocation>? _biomePalette;
            private readonly long[]? _biomeData;

            internal Section(NbtCompound nbt)
            {
                SectionY = nbt["Y"] is NbtByte yTag ? unchecked((sbyte)yTag.Value) : 0;
                (_blockStatePalette, _blockStateData) = ReadBlockStates(nbt);
                (_biomePalette, _biomeData) = ReadBiomes(nbt);
            }

            public void WriteBlocks(int chunkMinX, int chunkMinZ, Vector3Int min, Vector3Int max, int[] blocks,
                int sizeX, int sizeY, int sizeZ, System.Func<BlockState, int> getBlockId,
                out int invalidCount, out int totalCount)
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
                    isAirPalette[i] = entry.IsAir;
                    paletteIds[i] = getBlockId?.Invoke(entry) ?? 0;
                }

                var bits = GetBitsForBlocks(_blockStatePalette.Count, _blockStateData);
                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (bits == 0)
                {
                    if (isAirPalette[0]) return;
                    var id = paletteIds[0];
                    if (id <= 0) return;
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
                            var paletteIndex = GetBlockIndex(_blockStateData, index, bits);
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

            public void WriteBiomes(int chunkMinX, int chunkMinZ, Vector3Int min, Vector3Int max, int[] biomes,
                int sizeX, int sizeY, int sizeZ, System.Func<ResourceLocation, int> getBiomeIndex, int fallbackIndex)
            {
                if (_biomePalette == null || _biomePalette.Count == 0)
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
                    paletteIndices[i] = getBiomeIndex.Invoke(_biomePalette[i]);
                }

                var bits = GetBitsForBiomes(_biomePalette.Count, _biomeData);
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

            private static int GetBitsForBlocks(int paletteSize, long[]? blockStates)
            {
                if (paletteSize <= 1)
                {
                    return 0;
                }

                var bits = 4;
                while (1 << bits < paletteSize)
                {
                    bits++;
                }

                var dataBits = blockStates?.Length > 0
                    ? (int)System.Math.Floor((double)(blockStates.Length * 64) / 4096)
                    : 0;
                if (dataBits > bits)
                {
                    bits = dataBits;
                }

                return bits;
            }

            private static int GetBitsForBiomes(int paletteSize, long[]? data)
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

            private static int GetBlockIndex(long[] blockStates, int index, int bits)
            {
                if (bits <= 0 || blockStates.Length == 0)
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
