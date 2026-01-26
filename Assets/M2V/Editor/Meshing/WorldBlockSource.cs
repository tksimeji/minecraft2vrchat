using System;
using System.Collections.Generic;
using fNbt;
using M2V.Editor.Model;
using M2V.Editor.World;
using UnityEngine;
using BlockState = M2V.Editor.World.Block.BlockState;

namespace M2V.Editor.Meshing
{
    internal sealed class WorldBlockSource : IBlockStateSource
    {
        public long CountBlocksInRange(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max,
            ref bool logChunkOnce)
        {
            var world = GetWorld(worldFolder);
            if (world == null)
            {
                return 0;
            }

            if (!world.HasRegionData(dimensionId))
            {
                return 0;
            }

            var chunkMinX = FloorDiv(min.x, 16);
            var chunkMaxX = FloorDiv(max.x, 16);
            var chunkMinZ = FloorDiv(min.z, 16);
            var chunkMaxZ = FloorDiv(max.z, 16);

            long count = 0;
            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    count += CountBlocksInChunk(world, dimensionId, cx, cz, min, max, ref logChunkOnce);
                }
            }

            return count;
        }

        public bool FillBlockStateIds(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max,
            int[] blocks, int[] biomes, int sizeX, int sizeY, int sizeZ, List<BlockStateKey> states,
            BiomeRegistry biomeRegistry, ref bool logChunkOnce, bool logPaletteBounds)
        {
            var world = GetWorld(worldFolder);
            if (world == null)
            {
                return false;
            }

            if (!world.HasRegionData(dimensionId))
            {
                Debug.LogWarning($"[Minecraft2VRChat] Region data not found for dimension: {dimensionId}");
                return false;
            }

            var chunkMinX = FloorDiv(min.x, 16);
            var chunkMaxX = FloorDiv(max.x, 16);
            var chunkMinZ = FloorDiv(min.z, 16);
            var chunkMaxZ = FloorDiv(max.z, 16);

            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    FillBlockStateIdsInChunk(world, dimensionId, cx, cz, min, max, blocks, biomes, sizeX, sizeY, sizeZ,
                        states, biomeRegistry, ref logChunkOnce, logPaletteBounds);
                }
            }

            return true;
        }

        private static long CountBlocksInChunk(World.World world, string dimensionId, int chunkX, int chunkZ,
            Vector3Int min, Vector3Int max, ref bool logChunkOnce)
        {
            var chunk = ReadChunk(world, dimensionId, chunkX, chunkZ);
            if (chunk == null)
            {
                return 0;
            }

            if (logChunkOnce)
            {
                logChunkOnce = false;
                Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
            }

            var sections = chunk.Sections;
            if (sections.Count == 0)
            {
                return 0;
            }

            var chunkMinX = chunkX * 16;
            var chunkMinZ = chunkZ * 16;
            long count = 0;

            foreach (var section in sections)
            {
                if (!TryGetSectionY(section, out var sectionY))
                {
                    continue;
                }

                var sectionMinY = sectionY * 16;
                var sectionMaxY = sectionMinY + 15;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    continue;
                }

                if (!TryGetSectionBlockData(section, out var palette, out var blockStates))
                {
                    continue;
                }

                BuildPaletteCaches(palette, out var paletteNames, out var isAir);
                var bits = GetBitsForSection(palette.Count, blockStates);
                if (bits == 0)
                {
                    if (isAir[0])
                    {
                        continue;
                    }

                    var intersect = GetIntersection(chunkMinX, chunkMinZ, sectionMinY, min, max);
                    count += intersect;
                    continue;
                }

                if (blockStates == null || blockStates.Length == 0)
                {
                    continue;
                }

                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

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
                            var paletteIndex = GetBlockStateIndex(blockStates, index, bits);
                            if (paletteIndex >= 0 && paletteIndex < isAir.Length && !isAir[paletteIndex])
                            {
                                count++;
                            }
                        }
                    }
                }
            }

            return count;
        }

        private static void FillBlockStateIdsInChunk(World.World world, string dimensionId, int chunkX, int chunkZ,
            Vector3Int min, Vector3Int max, int[] blocks, int[] biomes, int sizeX, int sizeY, int sizeZ,
            List<BlockStateKey> states, BiomeRegistry biomeRegistry, ref bool logChunkOnce, bool logPaletteBounds)
        {
            var chunk = ReadChunk(world, dimensionId, chunkX, chunkZ);
            if (chunk == null)
            {
                return;
            }

            if (logChunkOnce)
            {
                logChunkOnce = false;
                Debug.Log($"[Minecraft2VRChat] Chunk NBT (sample):\n{chunk}");
            }

            var sections = chunk.Sections;
            if (sections == null || sections.Count == 0)
            {
                return;
            }

            var chunkMinX = chunkX * 16;
            var chunkMinZ = chunkZ * 16;

            foreach (var section in sections)
            {
                if (!TryGetSectionY(section, out var sectionY))
                {
                    continue;
                }

                var sectionMinY = sectionY * 16;
                var sectionMaxY = sectionMinY + 15;
                if (max.y < sectionMinY || min.y > sectionMaxY)
                {
                    continue;
                }

                if (!TryGetSectionBlockData(section, out var palette, out var blockStates))
                {
                    continue;
                }

                if (biomes != null && biomeRegistry != null)
                {
                    FillBiomeIdsInSection(section, biomeRegistry, chunkMinX, chunkMinZ, sectionMinY, min, max, biomes,
                        sizeX, sizeY, sizeZ);
                }

                var paletteIds = new int[palette.Count];
                var isAir = new bool[palette.Count];
                for (var i = 0; i < palette.Count; i++)
                {
                    var entry = palette[i];
                    var name = entry.Name;
                    isAir[i] = IsAirBlock(name);
                    paletteIds[i] = GetOrCreateBlockStateId(states, entry);
                }

                var bits = GetBitsForSection(palette.Count, blockStates);
                var xMin = Mathf.Max(min.x, chunkMinX);
                var xMax = Mathf.Min(max.x, chunkMinX + 15);
                var zMin = Mathf.Max(min.z, chunkMinZ);
                var zMax = Mathf.Min(max.z, chunkMinZ + 15);
                var yMin = Mathf.Max(min.y, sectionMinY);
                var yMax = Mathf.Min(max.y, sectionMaxY);

                if (bits == 0)
                {
                    if (!isAir[0])
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

                    continue;
                }

                if (blockStates == null || blockStates.Length == 0)
                {
                    continue;
                }

                var invalidCount = 0;
                var totalCount = 0;

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
                            var paletteIndex = GetBlockStateIndex(blockStates, index, bits);
                            totalCount++;
                            if (paletteIndex >= 0 && paletteIndex < paletteIds.Length)
                            {
                                var id = paletteIds[paletteIndex];
                                if (id > 0 && !isAir[paletteIndex])
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

                if (logPaletteBounds && invalidCount > 0)
                {
                    Debug.LogWarning(
                        $"[Minecraft2VRChat] Palette index out of range: {invalidCount}/{totalCount} (palette {paletteIds.Length}, bits {bits}, chunk {chunkX},{chunkZ}, sectionY {sectionY}).");
                }
            }
        }

        private static void FillBiomeIdsInSection(Chunk.Section section, BiomeRegistry biomeRegistry, int chunkMinX,
            int chunkMinZ, int sectionMinY, Vector3Int min, Vector3Int max, int[] biomes, int sizeX, int sizeY,
            int sizeZ)
        {
            if (!TryGetSectionBiomeData(section, out var biomePalette, out var biomeData) || biomePalette == null ||
                biomePalette.Count == 0)
            {
                return;
            }

            var paletteIndices = new int[biomePalette.Count];
            for (var i = 0; i < biomePalette.Count; i++)
            {
                paletteIndices[i] = biomeRegistry.GetBiomeIndex(biomePalette[i]);
            }

            var bits = GetBitsForBiomeSection(biomePalette.Count, biomeData);
            var xMin = Mathf.Max(min.x, chunkMinX);
            var xMax = Mathf.Min(max.x, chunkMinX + 15);
            var zMin = Mathf.Max(min.z, chunkMinZ);
            var zMax = Mathf.Min(max.z, chunkMinZ + 15);
            var yMin = Mathf.Max(min.y, sectionMinY);
            var yMax = Mathf.Min(max.y, sectionMinY + 15);

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

            if (biomeData == null || biomeData.Length == 0)
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
                        var paletteIndex = GetPackedIndex(biomeData, biomeIndex, bits);
                        var resolvedIndex = (paletteIndex >= 0 && paletteIndex < paletteIndices.Length)
                            ? paletteIndices[paletteIndex]
                            : biomeRegistry.PlainsIndex;
                        var outIndex = (x - min.x) + sizeX * ((y - min.y) + sizeY * (z - min.z));
                        if (outIndex >= 0 && outIndex < biomes.Length)
                        {
                            biomes[outIndex] = resolvedIndex;
                        }
                    }
                }
            }
        }

        private static int GetOrCreateBlockStateId(List<BlockStateKey> states, BlockState entry)
        {
            var name = entry.Name;
            if (string.IsNullOrEmpty(name) || IsAirBlock(name))
            {
                return 0;
            }

            var properties = ConvertProperties(entry.Properties);
            var key = BuildStateKey(name, properties);
            for (var i = 1; i < states.Count; i++)
            {
                if (states[i].Key == key)
                {
                    return i;
                }
            }

            var state = new BlockStateKey(name, properties, key);
            states.Add(state);
            return states.Count - 1;
        }

        private static string BuildStateKey(string name, IReadOnlyDictionary<string, string> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return name;
            }

            var keys = new List<string>(properties.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>(keys.Count);
            foreach (var key in keys)
            {
                parts.Add($"{key}={properties[key]}");
            }

            return $"{name}|{string.Join(";", parts)}";
        }

        private static Dictionary<string, string> ConvertProperties(IReadOnlyDictionary<string, NbtTag> properties)
        {
            if (properties == null || properties.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, string>(properties.Count, StringComparer.Ordinal);
            foreach (var kvp in properties)
            {
                result[kvp.Key] = FormatNbtValue(kvp.Value);
            }

            return result;
        }

        private static string FormatNbtValue(NbtTag tag)
        {
            if (tag == null)
            {
                return string.Empty;
            }

            return tag switch
            {
                NbtString str => str.Value ?? string.Empty,
                NbtByte b => b.Value == 0 ? "false" :
                    b.Value == 1 ? "true" : b.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NbtShort s => s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NbtInt i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NbtLong l => l.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NbtFloat f => f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NbtDouble d => d.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => tag.ToString()
            };
        }

        private static void BuildPaletteCaches(IReadOnlyList<BlockState> palette, out string[] names, out bool[] isAir)
        {
            names = new string[palette.Count];
            isAir = new bool[palette.Count];
            for (var i = 0; i < palette.Count; i++)
            {
                var name = palette[i].Name;
                names[i] = name;
                isAir[i] = IsAirBlock(name);
            }
        }

        private static bool IsAirBlock(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            return name == "minecraft:air" || name == "minecraft:cave_air" || name == "minecraft:void_air";
        }

        private static int GetBitsForSection(int paletteSize, long[] blockStates)
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

            var dataBits = blockStates?.Length > 0 ? (int)Math.Floor((double)(blockStates.Length * 64) / 4096) : 0;
            if (dataBits > bits)
            {
                bits = dataBits;
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

        private static bool TryGetSectionY(Chunk.Section section, out int sectionY)
        {
            sectionY = 0;
            if (section == null)
            {
                return false;
            }

            sectionY = section.Y;
            return true;
        }

        private static bool TryGetSectionBlockData(Chunk.Section section, out IReadOnlyList<BlockState> palette,
            out long[] blockStates)
        {
            palette = null;
            blockStates = null;
            if (section == null)
            {
                return false;
            }

            palette = section.BlockStatePalette;
            blockStates = section.BlockStateData;
            return palette != null;
        }

        private static bool TryGetSectionBiomeData(Chunk.Section section, out IReadOnlyList<string> palette,
            out long[] data)
        {
            palette = null;
            data = null;
            if (section == null)
            {
                return false;
            }

            palette = section.BiomePalette;
            data = section.BiomeData;
            return palette != null;
        }

        private static int GetBitsForBiomeSection(int paletteSize, long[] data)
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

        private static long GetIntersection(int chunkMinX, int chunkMinZ, int sectionMinY, Vector3Int min,
            Vector3Int max)
        {
            var xMin = Mathf.Max(min.x, chunkMinX);
            var xMax = Mathf.Min(max.x, chunkMinX + 15);
            var zMin = Mathf.Max(min.z, chunkMinZ);
            var zMax = Mathf.Min(max.z, chunkMinZ + 15);
            var yMin = Mathf.Max(min.y, sectionMinY);
            var yMax = Mathf.Min(max.y, sectionMinY + 15);
            if (xMin > xMax || yMin > yMax || zMin > zMax)
            {
                return 0;
            }

            return (long)(xMax - xMin + 1) * (yMax - yMin + 1) * (zMax - zMin + 1);
        }

        private static World.World GetWorld(string worldFolder)
        {
            var root = new System.IO.DirectoryInfo(worldFolder);
            return World.World.Of(root);
        }

        private static Chunk ReadChunk(World.World world, string dimensionId, int chunkX, int chunkZ)
        {
            if (world == null)
            {
                return null;
            }

            if (dimensionId == World.World.OverworldId)
            {
                return world.GetOverworldChunkAt(chunkX, chunkZ);
            }

            if (dimensionId == World.World.NetherId)
            {
                return world.GetNetherChunkAt(chunkX, chunkZ);
            }

            if (dimensionId == World.World.EndId)
            {
                return world.GetEndChunkAt(chunkX, chunkZ);
            }

            var region = world.GetRegionAt(dimensionId, chunkX, chunkZ);
            return region?.GetChunkAt(chunkX, chunkZ);
        }

        private static int FloorDiv(int value, int divisor)
        {
            var result = value / divisor;
            if ((value ^ divisor) < 0 && value % divisor != 0)
            {
                result--;
            }

            return result;
        }
    }
}
