#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using M2V.Editor.Minecraft.World;
using UnityEngine;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class Volume
    {
        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }
        public IReadOnlyList<BlockState> BlockStates { get; }

        public int TotalBlocks => _blocks.Length;
        public int BlockStateCount => BlockStates.Count;

        private readonly int[] _blocks;

        private readonly int[] _biomes;

        public static bool TryCreate(
            World world, LevelStem levelStem,
            Vector3Int min, Vector3Int max,
            BiomeIndex biomeRegistry,
            bool logPaletteBounds,
            ref bool logChunkOnce,
            out Volume volume,
            Action<float>? reportProgress = null,
            Action<int, int>? reportChunkCoords = null,
            System.Threading.CancellationToken cancellationToken = default
        )
        {
            volume = null!;

            var sizeX = max.x - min.x + 1;
            var sizeY = max.y - min.y + 1;
            var sizeZ = max.z - min.z + 1;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
                return false;

            var total = (long)sizeX * sizeY * sizeZ;
            var blockStates = new List<BlockState> { BlockState.Air };
            var blockStateIds = new Dictionary<BlockState, int> { [BlockState.Air] = 0 };
            var blocks = new int[total];
            var biomes = new int[total];
            Array.Fill(biomes, biomeRegistry.PlainsIndex);

            if (!TryFillBlockStateIds(
                    world, levelStem,
                    min, max,
                    blocks, biomes,
                    sizeX, sizeY, sizeZ,
                    blockStates, blockStateIds, biomeRegistry,
                    ref logChunkOnce,
                    logPaletteBounds,
                    reportProgress,
                    reportChunkCoords,
                    cancellationToken
                ))
            {
                return false;
            }

            volume = new Volume(sizeX, sizeY, sizeZ, blocks, biomes, blockStates);
            return true;
        }

        private Volume(
            int sizeX, int sizeY, int sizeZ,
            int[] blocks, int[] biomes,
            IReadOnlyList<BlockState> blockStates
        )
        {
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            _blocks = blocks;
            _biomes = biomes;
            BlockStates = blockStates;
        }

        public int GetBlockId(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= SizeX || y >= SizeY || z >= SizeZ)
                return 0;
            return _blocks[x + SizeX * (y + SizeY * z)];
        }

        public int GetBlockIdAtIndex(int index)
        {
            if (index < 0 || index >= _blocks.Length)
            {
                return 0;
            }

            return _blocks[index];
        }

        public int GetBiomeIdAtIndex(int index)
        {
            if (index < 0 || index >= _biomes.Length)
            {
                return 0;
            }

            return _biomes[index];
        }

        public bool HasSolidBlocks() => _blocks.Any(block => block > 0);

        private static bool TryFillBlockStateIds(
            World world, LevelStem levelStem,
            Vector3Int min, Vector3Int max,
            int[] blocks, int[] biomes,
            int sizeX, int sizeY, int sizeZ,
            List<BlockState> states,
            Dictionary<BlockState, int> stateIdsByKey,
            BiomeIndex biomeRegistry,
            ref bool logChunkOnce, bool logPaletteBounds,
            Action<float>? reportProgress,
            Action<int, int>? reportChunkCoords,
            System.Threading.CancellationToken cancellationToken
        )
        {
            if (!world.Has(levelStem))
            {
                Debug.LogWarning($"[M22V] Region data not found for dimension: {levelStem}");
                return false;
            }

            var chunkMinX = M2VMathHelper.FloorDiv(min.x, 16);
            var chunkMaxX = M2VMathHelper.FloorDiv(max.x, 16);
            var chunkMinZ = M2VMathHelper.FloorDiv(min.z, 16);
            var chunkMaxZ = M2VMathHelper.FloorDiv(max.z, 16);
            var totalChunks = (chunkMaxX - chunkMinX + 1) * (chunkMaxZ - chunkMinZ + 1);
            var processedChunks = 0;

            for (var cx = chunkMinX; cx <= chunkMaxX; cx++)
            {
                for (var cz = chunkMinZ; cz <= chunkMaxZ; cz++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    processedChunks++;
                    if (totalChunks > 0)
                    {
                        reportProgress?.Invoke((float)processedChunks / totalChunks);
                    }
                    reportChunkCoords?.Invoke(cx, cz);

                    var chunk = world.GetChunkAt(levelStem, cx, cz);
                    if (chunk == null)
                    {
                        continue;
                    }

                    if (logChunkOnce)
                    {
                        logChunkOnce = false;
                        Debug.Log($"Chunk NBT (sample):\n{chunk}");
                    }

                    var sections = chunk.Sections;
                    if (sections.Count == 0) continue;

                    var chunkMinXWorld = cx * 16;
                    var chunkMinZWorld = cz * 16;

                    foreach (var section in sections)
                    {
                        if (section == null)
                        {
                            continue;
                        }

                        if (biomes != null && biomeRegistry != null)
                        {
                            section.WriteBiomes(chunkMinXWorld, chunkMinZWorld, min, max, biomes, sizeX, sizeY, sizeZ,
                                biomeRegistry.GetIndex, biomeRegistry.PlainsIndex);
                        }

                        section.WriteBlocks(chunkMinXWorld, chunkMinZWorld, min, max, blocks, sizeX, sizeY, sizeZ,
                            state => GetOrCreateBlockStateId(states, stateIdsByKey, state),
                            out var invalidCount, out var totalCount);

                        if (logPaletteBounds && invalidCount > 0)
                        {
                            Debug.LogWarning(
                                $"Palette index out of range: {invalidCount}/{totalCount} (palette {section.BlockStatePalette?.Count ?? 0}, chunk {cx},{cz}, sectionY {section.SectionY})."
                            );
                        }
                    }
                }
            }

            return true;
        }

        private static int GetOrCreateBlockStateId(
            List<BlockState> states,
            Dictionary<BlockState, int> stateIdsByKey,
            BlockState entry
        )
        {
            var name = entry.Name;
            if (name.IsEmpty || entry.IsAir)
            {
                return 0;
            }

            if (stateIdsByKey.TryGetValue(entry, out var existing))
            {
                return existing;
            }

            var id = states.Count;
            states.Add(entry);
            stateIdsByKey[entry] = id;

            return id;
        }
    }
}