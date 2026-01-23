using System;
using System.Collections.Generic;
using System.IO;
using M2V.Editor.Model;
using UnityEngine;

namespace M2V.Editor.Meshing
{
    internal sealed class MeshingUseCase
    {
        private readonly IBlockStateSource _blockStateSource;
        private readonly IMeshBuilder _meshBuilder;
        private readonly ITextureAtlasBuilder _atlasBuilder;
        private readonly Func<IAssetReader, IModelRepository> _modelRepositoryFactory;

        internal MeshingUseCase(IBlockStateSource blockStateSource, IMeshBuilder meshBuilder, ITextureAtlasBuilder atlasBuilder, Func<IAssetReader, IModelRepository> modelRepositoryFactory)
        {
            _blockStateSource = blockStateSource;
            _meshBuilder = meshBuilder;
            _atlasBuilder = atlasBuilder;
            _modelRepositoryFactory = modelRepositoryFactory;
        }

        internal MeshingResult Execute(MeshingRequest request)
        {
            var result = new MeshingResult
            {
                Message = string.Empty,
                LogChunkOnce = request.LogChunkOnce
            };

            var sizeX = request.Max.x - request.Min.x + 1;
            var sizeY = request.Max.y - request.Min.y + 1;
            var sizeZ = request.Max.z - request.Min.z + 1;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            {
                result.Message = "[Minecraft2VRChat] Invalid range: size <= 0.";
                return result;
            }

            var volume = (long)sizeX * sizeY * sizeZ;
            // Removed hard cap to allow large ranges; caller should manage memory/timeout externally.

            var resourcePack = ResolveWorldResourcePack(request.WorldFolder);
            var dataPack = ResolveWorldDataPack(request.WorldFolder);
            var resolver = AssetPackResolver.TryCreate(request.MinecraftJarPath, resourcePack, dataPack, out var resolverError);
            if (resolver == null)
            {
                result.Message = resolverError;
                return result;
            }

            using (resolver)
            {
                var biomeRegistry = new BiomeRegistry(resolver.BiomeReader);

                var blockStates = new List<BlockStateKey> { BlockStateKey.Empty };
                var blocks = new int[volume];
                var biomes = new int[volume];
                Array.Fill(biomes, biomeRegistry.PlainsIndex);
                var logChunkOnce = result.LogChunkOnce;
                if (!_blockStateSource.FillBlockStateIds(request.WorldFolder, request.DimensionId, request.Min, request.Max, blocks, biomes, sizeX, sizeY, sizeZ, blockStates, biomeRegistry, ref logChunkOnce, request.Options.LogPaletteBounds))
                {
                    result.Message = "[Minecraft2VRChat] Failed to read blocks for meshing (region folder missing or read failure).";
                    result.LogChunkOnce = logChunkOnce;
                    return result;
                }

                result.LogChunkOnce = logChunkOnce;

                if (request.Options.LogSliceStats)
                {
                    LogSolidSliceStats(blocks, sizeX, sizeY, sizeZ, request.Min);
                }

                if (!HasSolidBlocks(blocks))
                {
                    var hint = $"[Minecraft2VRChat] No solid blocks found in range (all air or missing data). Blocks: {blocks.Length}, States: {blockStates.Count}.";
                    result.Message = hint;
                    return result;
                }

                var repository = _modelRepositoryFactory(resolver.ModelReader);
                var modelCache = repository.BuildBlockModels(blockStates);
                var fullCubeById = repository.BuildFullCubeFlags(modelCache);
                var texturePaths = repository.CollectTexturePaths(modelCache);

                var tintResolver = new BlockTintResolver(resolver.ModelReader, biomeRegistry);
                var tintByBlock = tintResolver.BuildTintByBlock(blocks, sizeX, sizeY, sizeZ, blockStates, biomes);

                result.AtlasTexture = _atlasBuilder.BuildTextureAtlasFromTextures(resolver.ModelReader, texturePaths, out var uvByTexture, out var alphaByTexture);
                if (result.AtlasTexture == null)
                {
                    result.Message = "[Minecraft2VRChat] Failed to build texture atlas from models.";
                    return result;
                }

                result.Mesh = _meshBuilder.BuildModelMesh(blocks, sizeX, sizeY, sizeZ, request.Min, modelCache, fullCubeById, tintByBlock, uvByTexture, alphaByTexture, request.Options.ApplyCoordinateTransform);

                if (result.Mesh == null)
                {
                    result.Message = "[Minecraft2VRChat] Mesh generation produced 0 faces (all air or no data).";
                }

                return result;
            }
        }

        private static void LogSolidSliceStats(int[] blocks, int sizeX, int sizeY, int sizeZ, Vector3Int min)
        {
            var dims = new[] { sizeX, sizeY, sizeZ };
            for (var z = 0; z < sizeZ; z++)
            {
                var count = 0;
                for (var y = 0; y < sizeY; y++)
                {
                    for (var x = 0; x < sizeX; x++)
                    {
                        var id = blocks[x + dims[0] * (y + dims[1] * z)];
                        if (id > 0)
                        {
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    Debug.Log($"[Minecraft2VRChat] Solids slice z={min.z + z}: {count} blocks.");
                }
            }
        }

        private static bool HasSolidBlocks(int[] blocks)
        {
            for (var i = 0; i < blocks.Length; i++)
            {
                if (blocks[i] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static FileSystemInfo ResolveWorldResourcePack(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return null;
            }

            var zipPath = Path.Combine(worldFolder, "resources.zip");
            if (File.Exists(zipPath))
            {
                return new FileInfo(zipPath);
            }

            var folderPath = Path.Combine(worldFolder, "resources");
            if (Directory.Exists(folderPath))
            {
                return new DirectoryInfo(folderPath);
            }

            return null;
        }

        private static FileSystemInfo ResolveWorldDataPack(string worldFolder)
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
