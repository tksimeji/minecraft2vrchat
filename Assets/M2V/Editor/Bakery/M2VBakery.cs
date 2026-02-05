#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using M2V.Editor.Bakery.Meshing;
using M2V.Editor.Minecraft;
using M2V.Editor.Minecraft.World;
using DomainWorld = M2V.Editor.Minecraft.World.World;

namespace M2V.Editor.Bakery
{
    public sealed class M2VBakery : IDisposable
    {
        private readonly List<IDisposable> _disposables = new();
        private AssetReader _modelReader = null!;
        private AssetReader _biomeReader = null!;
        private DomainWorld _world = null!;
        private bool _initialized;

        private M2VBakery()
        {
        }

        public static M2VBakery? Create(BakeryContext context, out string message)
        {
            var bakery = new M2VBakery();
            if (bakery.TryInitialize(context, out message)) return bakery;
            bakery.Dispose();
            return null;
        }

        public Baked Bake(BakeryContext context)
        {
            return ExecuteMeshing(context);
        }

        public void Dispose()
        {
            foreach (var disposable in _disposables)
            {
                disposable.Dispose();
            }

            _disposables.Clear();
        }

        private bool TryInitialize(BakeryContext context, out string message)
        {
            var resourcePack = WorldLoader.FindResourcePack(context.WorldFolder);
            var dataPack = WorldLoader.FindDataPack(context.WorldFolder);

            if (!TryCreateReaders(
                    context.MinecraftJarPath,
                    resourcePack,
                    dataPack,
                    out var modelReader,
                    out var biomeReader,
                    out var disposables,
                    out var resolverError
                ))
            {
                message = resolverError;
                return false;
            }

            if (modelReader == null || biomeReader == null)
            {
                message = "Assets not available for meshing.";
                return false;
            }

            _modelReader = modelReader;
            _biomeReader = biomeReader;
            _disposables.Clear();
            _disposables.AddRange(disposables);

            _world = WorldLoader.LoadWorld(context.WorldFolder);
            if (_world == null)
            {
                message = "World not found or invalid.";
                return false;
            }

            _initialized = true;
            message = string.Empty;
            return true;
        }

        private static bool TryCreateReaders(
            string minecraftJarPath,
            FileSystemInfo? resourcePack,
            FileSystemInfo? dataPack,
            out AssetReader? modelReader,
            out AssetReader? biomeReader,
            out List<IDisposable> disposables,
            out string error
        )
        {
            modelReader = null;
            biomeReader = null;
            disposables = new List<IDisposable>();
            error = string.Empty;

            if (string.IsNullOrEmpty(minecraftJarPath))
            {
                error = "Minecraft jar path is missing.";
                return false;
            }

            var jarFile = new FileInfo(minecraftJarPath);
            if (!jarFile.Exists)
            {
                error = $"Minecraft jar not found: {minecraftJarPath}";
                return false;
            }

            if (!TryCreateReader(jarFile, out var jarReader, out var jarError))
            {
                error = jarError;
                return false;
            }

            if (jarReader == null)
            {
                error = "Failed to read Minecraft jar.";
                return false;
            }

            if (jarReader is IDisposable jarDisposable)
            {
                disposables.Add(jarDisposable);
            }

            var modelReaders = new List<AssetReader>();

            if (TryCreateReader(resourcePack, out var resourceReader, out _) && resourceReader != null)
            {
                modelReaders.Add(resourceReader);
                if (resourceReader is IDisposable resourceDisposable)
                {
                    disposables.Add(resourceDisposable);
                }
            }

            var biomeReaders = EnumerateDataPackReaders(dataPack, disposables).ToList();

            modelReaders.Add(jarReader);
            biomeReaders.Add(jarReader);

            modelReader = new CompositeAssetReader(modelReaders);
            biomeReader = new CompositeAssetReader(biomeReaders);
            return true;
        }

        private static bool TryCreateReader(FileSystemInfo? info, out AssetReader? reader, out string error)
        {
            reader = null;
            error = string.Empty;
            if (info == null)
            {
                return false;
            }

            if (info is DirectoryInfo { Exists: true } directory)
            {
                reader = new DirectoryAssetReader(directory);
                return true;
            }

            if (info is FileInfo { Exists: true } file)
            {
                reader = new ZipAssetReader(file);
                return true;
            }

            if (info is FileInfo missingFile)
            {
                error = $"Pack file not found: {missingFile.FullName}";
            }
            else if (info is DirectoryInfo missingDirectory)
            {
                error = $"Pack folder not found: {missingDirectory.FullName}";
            }

            return false;
        }

        private static IEnumerable<AssetReader> EnumerateDataPackReaders(
            FileSystemInfo? dataPack,
            List<IDisposable> disposables
        )
        {
            switch (dataPack)
            {
                case null:
                    yield break;
                case FileInfo file:
                {
                    if (!TryCreateReader(file, out var reader, out _)) yield break;
                    if (reader is IDisposable disposable)
                    {
                        disposables.Add(disposable);
                    }

                    if (reader != null)
                    {
                        yield return reader;
                    }

                    yield break;
                }
            }

            if (dataPack is not DirectoryInfo { Exists: true } root)
            {
                yield break;
            }

            var packInfos = root.EnumerateDirectories().Cast<FileSystemInfo>().ToList();
            packInfos.AddRange(root.EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly));
            packInfos.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            packInfos.Reverse();

            foreach (var info in packInfos)
            {
                if (!TryCreateReader(info, out var reader, out _))
                {
                    continue;
                }

                if (reader is IDisposable disposable)
                {
                    disposables.Add(disposable);
                }

                if (reader != null)
                {
                    yield return reader;
                }
            }
        }

        private Baked ExecuteMeshing(BakeryContext context)
        {
            var result = new Baked
            {
                Message = string.Empty,
                LogChunkOnce = context.LogChunkOnce
            };

            if (!_initialized)
            {
                result.Message = "Bakery is not initialized.";
                return result;
            }

            var min = context.Min;
            var max = context.Max;
            var sizeX = max.x - min.x + 1;
            var sizeY = max.y - min.y + 1;
            var sizeZ = max.z - min.z + 1;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            {
                result.Message = "Invalid range: size <= 0.";
                return result;
            }

            var world = _world;
            var modelReader = _modelReader;
            var biomeReader = _biomeReader;

            var biomeRegistry = new BiomeIndex(biomeReader);
            var logOnce = result.LogChunkOnce;
            if (!Volume.TryCreate(world, context.LevelStem, min, max, biomeRegistry,
                    context.LogPaletteBounds, ref logOnce, out var volume))
            {
                result.Message = "Failed to read blocks for meshing (region folder missing or read failure).";
                result.LogChunkOnce = logOnce;
                return result;
            }

            result.LogChunkOnce = logOnce;

            if (context.LogSliceStats)
            {
                LogSolidSliceStats(volume, min);
            }

            if (!volume.HasSolidBlocks())
            {
                var hint =
                    $"No solid blocks found in range (all air or missing data). Blocks: {volume.TotalBlocks}, States: {volume.BlockStateCount}.";
                result.Message = hint;
                return result;
            }

            var modelResolver = new BlockModelResolver(modelReader);
            var buildResult = MeshBuilder.Build(
                volume,
                Vector3Int.zero,
                modelResolver,
                biomeRegistry,
                modelReader,
                context.ApplyCoordinateTransform
            );
            result.AtlasTexture = buildResult.AtlasTexture;
            result.Mesh = buildResult.Mesh;
            result.AtlasAnimation = buildResult.AtlasAnimation;

            if (result.Mesh == null)
            {
                result.Message = "Mesh generation produced 0 faces (all air or no data).";
            }

            return result;
        }

        private static void LogSolidSliceStats(Volume volume, Vector3Int min)
        {
            var sizeX = volume.SizeX;
            var sizeY = volume.SizeY;
            var sizeZ = volume.SizeZ;
            for (var z = 0; z < sizeZ; z++)
            {
                var count = 0;
                for (var y = 0; y < sizeY; y++)
                {
                    for (var x = 0; x < sizeX; x++)
                    {
                        var id = volume.GetBlockId(x, y, z);
                        if (id > 0)
                        {
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    Debug.Log($"Solids slice z={min.z + z}: {count} blocks.");
                }
            }
        }
    }

    public sealed record BakeryContext
    {
        public string WorldFolder { get; init; } = string.Empty;
        public string MinecraftJarPath { get; init; } = string.Empty;
        public LevelStem LevelStem { get; init; }
        public Vector3Int Min { get; init; }
        public Vector3Int Max { get; init; }
        public bool UseGreedy { get; init; }
        public bool ApplyCoordinateTransform { get; init; }
        public bool LogSliceStats { get; init; }
        public bool LogPaletteBounds { get; init; }
        public bool UseTextureAtlas { get; init; }
        public bool LogChunkOnce { get; init; }
    }

        public sealed record Baked
        {
            public Mesh? Mesh { get; set; }
            public Texture2D? AtlasTexture { get; set; }
            public AtlasAnimation? AtlasAnimation { get; set; }
            public string Message { get; set; } = string.Empty;
            public bool LogChunkOnce { get; set; }
        }
}