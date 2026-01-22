using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

            if (string.IsNullOrEmpty(request.MinecraftJarPath) || !File.Exists(request.MinecraftJarPath))
            {
                result.Message = "[Minecraft2VRChat] Minecraft version jar not found for model lookup.";
                return result;
            }

            using var zip = ZipFile.OpenRead(request.MinecraftJarPath);
            var openedZips = new List<ZipArchive>();
            try
            {
                var jarReader = new ZipAssetReader(zip);
                var resourceReader = BuildAssetReader(request.ResourcePack, openedZips);
                var dataReader = BuildAssetReader(request.DataPack, openedZips);
                var modelReader = resourceReader != null
                    ? new AssetReaderChain(resourceReader, jarReader)
                    : new AssetReaderChain(jarReader);
                var biomeReader = dataReader != null
                    ? new AssetReaderChain(dataReader, jarReader)
                    : new AssetReaderChain(jarReader);
                var biomeRegistry = new BiomeRegistry(biomeReader);

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

                var repository = _modelRepositoryFactory(modelReader);
                var modelCache = repository.BuildBlockModels(blockStates);
                var fullCubeById = repository.BuildFullCubeFlags(modelCache);
                var texturePaths = repository.CollectTexturePaths(modelCache);

                var tintResolver = new BlockTintResolver(modelReader, biomeRegistry);
                var tintByBlock = tintResolver.BuildTintByBlock(blocks, sizeX, sizeY, sizeZ, blockStates, biomes);

                result.AtlasTexture = _atlasBuilder.BuildTextureAtlasFromTextures(modelReader, texturePaths, out var uvByTexture, out var alphaByTexture);
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
            finally
            {
                foreach (var extra in openedZips)
                {
                    extra?.Dispose();
                }
            }
        }

        private static IAssetReader BuildAssetReader(FileSystemInfo pack, List<ZipArchive> openedZips)
        {
            if (pack == null)
            {
                return null;
            }

            if (pack is FileInfo fileInfo)
            {
                if (!fileInfo.Exists)
                {
                    return null;
                }

                if (string.Equals(fileInfo.Extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var zip = ZipFile.OpenRead(fileInfo.FullName);
                    openedZips.Add(zip);
                    return new ZipAssetReader(zip);
                }

                return null;
            }

            if (pack is DirectoryInfo dirInfo)
            {
                if (!dirInfo.Exists)
                {
                    return null;
                }

                return new FolderAssetReader(dirInfo);
            }

            return null;
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
    }
}
