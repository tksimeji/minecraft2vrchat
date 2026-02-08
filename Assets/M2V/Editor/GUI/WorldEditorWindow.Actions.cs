#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Bakery;
using M2V.Editor.Bakery.Meshing;
using M2V.Editor.Bakery.Tinting;
using UnityEngine.UIElements;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private const string DiscordInviteUrl = "https://discord.com/invite/Dus2WmpvUE";
        private const int ChunkGroupSize = 4;
        private MeshingJob? _meshingJob;
        private IVisualElementScheduledItem? _meshingUpdate;

        private enum MeshingStage
        {
            None,
            Preparing,
            BuildingAtlas,
            BuildingMesh,
            Applying,
            Done,
            Failed
        }

        private sealed class MeshingJob
        {
            public readonly ConcurrentQueue<ChunkCoord> ChunkQueue = new();
            public readonly ConcurrentQueue<ChunkCoord> LoadedChunkQueue = new();
            public MeshingStage Stage;
            public BakeryContext Context = null!;
            public M2VBakery? Session;
            public BlockTintResolver.ColormapSet? Colormaps;
            public PreparedMeshing? Prepared;
            public List<MeshBuilder.MeshChunkData>? MeshChunks;
            public Texture2D? AtlasTexture;
            public AtlasAnimation? AtlasAnimation;
            public Dictionary<M2V.Editor.Minecraft.ResourceLocation, RectF>? UvByTexture;
            public Dictionary<M2V.Editor.Minecraft.ResourceLocation, TextureAlphaMode>? AlphaByTexture;
            public string ErrorMessage = string.Empty;
            public int ProgressCurrent;
            public int ProgressTotal;
            public ProgressMeter Meter = new();
            public int ChunkMinX;
            public int ChunkMinZ;
            public int ChunkWidth;
            public int ChunkHeight;
            public int ChunkGroupSize;
            public Texture2D? ChunkMapTexture;
            public Color32[]? ChunkMapPixels;
            public Queue<MeshBuilder.MeshChunkData>? ApplyQueue;
            public List<MeshInstaller.ChunkMesh>? BuiltChunks;
            public CancellationTokenSource Cancellation = new();
            public bool CancelRequested;
            public Task? PrepareTask;
            public Task? MeshTask;
        }
        private readonly struct ChunkCoord
        {
            public readonly int X;
            public readonly int Z;

            public ChunkCoord(int x, int z)
            {
                X = x;
                Z = z;
            }
        }
        private sealed class ProgressMeter
        {
            private const float PrepareWeight = 0.2f;
            private const float AtlasWeight = 0.15f;
            private const float MeshWeight = 0.6f;
            private const float ApplyWeight = 0.05f;

            private float _overall;

            public float Overall => _overall;

            public void Report(MeshingStage stage, float progress)
            {
                var clamped = Mathf.Clamp01(progress);
                _overall = stage switch
                {
                    MeshingStage.Preparing => clamped * PrepareWeight,
                    MeshingStage.BuildingAtlas => PrepareWeight + clamped * AtlasWeight,
                    MeshingStage.BuildingMesh => PrepareWeight + AtlasWeight + clamped * MeshWeight,
                    MeshingStage.Applying => PrepareWeight + AtlasWeight + MeshWeight + clamped * ApplyWeight,
                    MeshingStage.Done => 1f,
                    _ => _overall
                };
            }
        }

        private void OnClickCustomImport()
        {
            var startPath = string.IsNullOrEmpty(_state.GetSelectedPath())
                ? GetDefaultWorldsPath()
                : _state.GetSelectedPath();
            var selected = EditorUtility.OpenFolderPanel("Select Minecraft World Folder", startPath, "");
            if (!string.IsNullOrEmpty(selected))
            {
                var selectedDir = new DirectoryInfo(selected);
                if (!SelectWorldInList(selectedDir))
                {
                    _worldList.ClearSelection();
                    _state.SetSelectedWorld(selectedDir);
                }
                UpdateValidation();
            }
        }
        private void OnClickReload()
        {
            RefreshWorldList();
            _state.CurrentWorldPath = null;
            UpdateValidation();
        }
        private void OnClickOpen()
        {
            var path = _state.GetSelectedPath();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
            }
        }
        private void OnClickClear()
        {
            _worldList.ClearSelection();
            _state.SetSelectedWorld(null);
            UpdateValidation();
        }
        private void OnClickGenerateMesh()
        {
            if (_meshingJob != null)
            {
                return;
            }

            StartMeshGeneration();
        }
        private void OnClickDiscord()
        {
            Application.OpenURL(DiscordInviteUrl);
        }
        private void CancelMeshing()
        {
            if (_meshingJob == null)
            {
                return;
            }

            _meshingJob.CancelRequested = true;
            _meshingJob.Cancellation.Cancel();
        }
        private void StartMeshGeneration()
        {
            var path = _state.GetSelectedPath();
            var worldDir = _state.GetSelectedWorld();
            if (worldDir == null)
            {
                EditorUtility.DisplayDialog(Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                    Localization.Get(_state.Language, Localization.Keys.DialogSelectWorld),
                    "OK");
                return;
            }

            var dimensionId = GetSelectedDimensionId();
            if (!TryGetRange(out var min, out var max))
            {
                EditorUtility.DisplayDialog(Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                    Localization.Get(_state.Language, Localization.Keys.DialogEnterRange),
                    "OK");
                return;
            }

            var versionName = worldDir.VersionName;
            var jarPath = GetMinecraftVersionJarPath(versionName);
            if (string.IsNullOrEmpty(jarPath))
            {
                EditorUtility.DisplayDialog(Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                    Localization.Get(_state.Language, Localization.Keys.DialogJarMissing),
                    "OK");
                return;
            }

            var context = new BakeryContext
            {
                WorldFolder = path,
                MinecraftJarPath = jarPath,
                LevelStem = dimensionId,
                Min = min,
                Max = max,
                UseGreedy = false,
                ApplyCoordinateTransform = true,
                BlockScale = _state.BlockScale,
                LogSliceStats = false,
                LogPaletteBounds = false,
                UseTextureAtlas = true,
                LogChunkOnce = _sLOGChunkDatOnce
            };

            ShowLoadingOverlay();
            SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingReadingBlocks));
            SetLoadingProgress(0f);

            var session = M2VBakery.Create(context, out var sessionMessage);
            if (session == null)
            {
                var dialogMessage = string.IsNullOrEmpty(sessionMessage)
                    ? Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed)
                    : sessionMessage;
                Debug.LogWarning(dialogMessage);
                EditorUtility.DisplayDialog(
                    Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                    dialogMessage,
                    "OK"
                );
                return;
            }

            var colormaps = BlockTintResolver.LoadColormaps(session.ModelReader);

            var job = new MeshingJob
            {
                Stage = MeshingStage.Preparing,
                Context = context,
                Session = session,
                Colormaps = colormaps,
                Meter = new ProgressMeter()
            };
            _meshingJob = job;
            InitializeChunkMap(job, min, max);
            job.Meter.Report(MeshingStage.Preparing, 0f);
            job.PrepareTask = Task.Run(() => PrepareMeshingJob(job));
            StartMeshingUpdateLoop();
        }

        private void StartMeshingUpdateLoop()
        {
            _meshingUpdate?.Pause();
            _meshingUpdate = rootVisualElement.schedule.Execute(UpdateMeshingJob).Every(16);
        }

        private void UpdateMeshingJob()
        {
            if (_meshingJob == null)
            {
                _meshingUpdate?.Pause();
                return;
            }

            var job = _meshingJob;
            if (job.CancelRequested)
            {
                SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingCancelling));
                if ((job.PrepareTask == null || job.PrepareTask.IsCompleted) &&
                    (job.MeshTask == null || job.MeshTask.IsCompleted))
                {
                    FinishMeshingJob();
                }
                return;
            }
            UpdateChunkMap(job);
            if (!string.IsNullOrEmpty(job.ErrorMessage))
            {
                FailMeshingJob(job.ErrorMessage);
                return;
            }

            SetLoadingProgress(job.Meter.Overall);
            UpdateLoadingStatus(job);

            if (job.Stage == MeshingStage.Preparing && job.PrepareTask?.IsCompleted == true)
            {
                if (job.Prepared == null)
                {
                    var message = string.IsNullOrEmpty(job.ErrorMessage)
                        ? Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed)
                        : job.ErrorMessage;
                    FailMeshingJob(message);
                    return;
                }

                job.Stage = MeshingStage.BuildingAtlas;
                BuildAtlasOnMainThread(job);
                return;
            }

            if (job.Stage == MeshingStage.BuildingMesh && job.MeshTask?.IsCompleted == true)
            {
                if (job.MeshChunks == null)
                {
                    var message = string.IsNullOrEmpty(job.ErrorMessage)
                        ? Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed)
                        : job.ErrorMessage;
                    FailMeshingJob(message);
                    return;
                }

                BeginApplyChunks(job);
                return;
            }

            if (job.Stage == MeshingStage.Applying)
            {
                ApplyChunksStep(job);
            }
        }

        private void UpdateLoadingStatus(MeshingJob job)
        {
            var label = job.Stage switch
            {
                MeshingStage.Preparing => Localization.Get(_state.Language, Localization.Keys.LoadingReadingBlocks),
                MeshingStage.BuildingAtlas => Localization.Get(_state.Language, Localization.Keys.LoadingGeneratingMesh),
                MeshingStage.BuildingMesh => Localization.Get(_state.Language, Localization.Keys.LoadingGeneratingMesh),
                MeshingStage.Applying => Localization.Get(_state.Language, Localization.Keys.LoadingApplyingMaterial),
                _ => Localization.Get(_state.Language, Localization.Keys.LoadingPreparing)
            };

            var percent = Mathf.Clamp01(job.Meter.Overall) * 100f;
            var detail = job.ProgressTotal > 0
                ? $" {job.ProgressCurrent}/{job.ProgressTotal}"
                : string.Empty;
            SetLoadingStatus($"{label} {percent:0}%{detail}");
        }

        private void PrepareMeshingJob(MeshingJob job)
        {
            try
            {
                if (job.CancelRequested)
                {
                    return;
                }

                if (job.Session == null || job.Colormaps == null)
                {
                    job.ErrorMessage = "Mesh generation failed.";
                    return;
                }

                var prepared = job.Session.PrepareMeshing(
                    job.Context,
                    job.Colormaps,
                    out var message,
                    progress => job.Meter.Report(MeshingStage.Preparing, progress),
                    (chunkX, chunkZ) => job.LoadedChunkQueue.Enqueue(new ChunkCoord(chunkX, chunkZ)),
                    job.Cancellation.Token
                );
                if (prepared == null)
                {
                    job.ErrorMessage = string.IsNullOrEmpty(message)
                        ? "Mesh generation failed."
                        : message;
                    return;
                }

                job.Prepared = prepared;
                job.Meter.Report(MeshingStage.Preparing, 1f);
            }
            catch (Exception ex)
            {
                job.ErrorMessage = ex.Message;
            }
        }

        private void BuildAtlasOnMainThread(MeshingJob job)
        {
            try
            {
                if (job.CancelRequested)
                {
                    return;
                }

                if (job.Prepared == null || job.Session == null)
                {
                    job.ErrorMessage = "Mesh generation failed.";
                    return;
                }

                job.Stage = MeshingStage.BuildingAtlas;
                job.Meter.Report(MeshingStage.BuildingAtlas, 0f);
                job.AtlasTexture = AtlasBuilder.BuildAtlas(
                    job.Prepared.ModelReader,
                    job.Prepared.TexturePaths,
                    out var uvByTexture,
                    out var alphaByTexture,
                    out var atlasAnimation,
                    progress =>
                    {
                        job.Meter.Report(MeshingStage.BuildingAtlas, progress);
                        job.ProgressCurrent = Mathf.RoundToInt(progress * 100f);
                        job.ProgressTotal = 100;
                    }
                );
                job.UvByTexture = uvByTexture;
                job.AlphaByTexture = alphaByTexture;
                job.AtlasAnimation = atlasAnimation;

                job.Stage = MeshingStage.BuildingMesh;
                job.Meter.Report(MeshingStage.BuildingMesh, 0f);
                job.MeshTask = Task.Run(() => BuildMeshDataJob(job));
            }
            catch (Exception ex)
            {
                job.ErrorMessage = ex.Message;
            }
        }

        private void BuildMeshDataJob(MeshingJob job)
        {
            try
            {
                if (job.CancelRequested)
                {
                    return;
                }

                if (job.Prepared == null || job.UvByTexture == null || job.AlphaByTexture == null)
                {
                    job.ErrorMessage = "Mesh generation failed.";
                    return;
                }

                var meshChunks = MeshBuilder.BuildMeshDataByChunk(
                    job.Prepared.Volume,
                    job.Context.Min,
                    Vector3Int.zero,
                    job.Prepared.BlockModelIndex,
                    job.Prepared.BlockModelResolver,
                    job.Prepared.FullCubeById,
                    job.Prepared.TintByBlock,
                    job.UvByTexture,
                    job.AlphaByTexture,
                    job.Context.ApplyCoordinateTransform,
                    ChunkGroupSize,
                    progress => job.Meter.Report(MeshingStage.BuildingMesh, progress),
                    (current, total) =>
                    {
                        job.ProgressCurrent = current;
                        job.ProgressTotal = total;
                    },
                    (chunkX, chunkZ) => job.ChunkQueue.Enqueue(new ChunkCoord(chunkX, chunkZ)),
                    job.Cancellation.Token
                );
                job.MeshChunks = meshChunks;
                job.Meter.Report(MeshingStage.BuildingMesh, 1f);
            }
            catch (Exception ex)
            {
                job.ErrorMessage = ex.Message;
            }
        }

        private void ApplyMeshOnMainThread(MeshingJob job)
        {
            if (job.CancelRequested)
            {
                return;
            }

            BeginApplyChunks(job);
        }

        private void BeginApplyChunks(MeshingJob job)
        {
            if (job.MeshChunks == null || job.MeshChunks.Count == 0)
            {
                FailMeshingJob(Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed));
                return;
            }

            job.Stage = MeshingStage.Applying;
            job.Meter.Report(MeshingStage.Applying, 0f);
            job.ApplyQueue = new Queue<MeshBuilder.MeshChunkData>(job.MeshChunks);
            job.BuiltChunks = new List<MeshInstaller.ChunkMesh>();
            job.ProgressCurrent = 0;
            job.ProgressTotal = job.MeshChunks.Count;
        }

        private void ApplyChunksStep(MeshingJob job)
        {
            if (job.CancelRequested)
            {
                return;
            }

            if (job.ApplyQueue == null || job.BuiltChunks == null)
            {
                FailMeshingJob(Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed));
                return;
            }

            var processed = 0;
            var budget = 4;
            while (job.ApplyQueue.Count > 0 && processed < budget)
            {
                var chunk = job.ApplyQueue.Dequeue();
                if (chunk.Data != null)
                {
                    var mesh = MeshBuilder.CreateMesh(chunk.Data.Buffer);
                    if (mesh != null)
                    {
                        var name = $"ChunkGroup_{chunk.ChunkX}_{chunk.ChunkZ}";
                        job.BuiltChunks.Add(new MeshInstaller.ChunkMesh(name, mesh));
                    }
                }

                processed++;
                job.ProgressCurrent++;
                job.Meter.Report(MeshingStage.Applying,
                    job.ProgressTotal == 0 ? 1f : (float)job.ProgressCurrent / job.ProgressTotal);
            }

            if (job.ApplyQueue.Count > 0)
            {
                return;
            }

            if (job.BuiltChunks.Count == 0)
            {
                FailMeshingJob(Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed));
                return;
            }

            var go = MeshInstaller.InstallChunkMeshes(
                "WorldMesh",
                job.BuiltChunks,
                job.AtlasTexture,
                job.AtlasAnimation,
                job.Context.BlockScale
            );

            if (job.Prepared != null)
            {
                _sLOGChunkDatOnce = job.Prepared.LogChunkOnce;
            }

            Selection.activeObject = go;
            var modeLabel = job.Context.UseGreedy ? "Greedy" : "Naive";
            var vertexCount = job.BuiltChunks.Sum(c => c.Mesh.vertexCount);
            Debug.Log($"[Minecraft2VRChat] {modeLabel} mesh generated. Vertices: {vertexCount}");
            job.Stage = MeshingStage.Done;
            job.Meter.Report(MeshingStage.Done, 1f);
            FinishMeshingJob();
        }

        private void FailMeshingJob(string message)
        {
            if (string.Equals(message, "Cancelled.", StringComparison.Ordinal))
            {
                FinishMeshingJob();
                return;
            }

            Debug.LogWarning(message);
            EditorUtility.DisplayDialog(
                Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                message,
                "OK"
            );
            FinishMeshingJob();
        }

        private void FinishMeshingJob()
        {
            _meshingUpdate?.Pause();
            _meshingUpdate = null;

            if (_meshingJob?.Session != null)
            {
                _meshingJob.Session.Dispose();
            }

            if (_loadingMap != null)
            {
                if (_meshingJob?.ChunkMapTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(_meshingJob.ChunkMapTexture);
                }

                _loadingMap.image = null;
            }

            _meshingJob = null;
            HideLoadingOverlay();
        }

        private void InitializeChunkMap(MeshingJob job, Vector3Int min, Vector3Int max)
        {
            if (_loadingMap == null)
            {
                return;
            }

            var chunkMinX = M2VMathHelper.FloorDiv(min.x, 16);
            var chunkMaxX = M2VMathHelper.FloorDiv(max.x, 16);
            var chunkMinZ = M2VMathHelper.FloorDiv(min.z, 16);
            var chunkMaxZ = M2VMathHelper.FloorDiv(max.z, 16);

            var chunkCountX = Mathf.Max(1, chunkMaxX - chunkMinX + 1);
            var chunkCountZ = Mathf.Max(1, chunkMaxZ - chunkMinZ + 1);
            var groupSize = ChunkGroupSize;
            var width = Mathf.Max(1, Mathf.CeilToInt(chunkCountX / (float)groupSize));
            var height = Mathf.Max(1, Mathf.CeilToInt(chunkCountZ / (float)groupSize));

            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false);

            _loadingMap.image = texture;
            _loadingMap.scaleMode = ScaleMode.ScaleToFit;

            job.ChunkMinX = chunkMinX;
            job.ChunkMinZ = chunkMinZ;
            job.ChunkWidth = width;
            job.ChunkHeight = height;
            job.ChunkGroupSize = groupSize;
            job.ChunkMapTexture = texture;
            job.ChunkMapPixels = pixels;
        }

        private void UpdateChunkMap(MeshingJob job)
        {
            if (job.ChunkMapTexture == null || job.ChunkMapPixels == null)
            {
                return;
            }

            var changed = false;
            while (job.LoadedChunkQueue.TryDequeue(out var coord))
            {
                var x = (coord.X - job.ChunkMinX) / job.ChunkGroupSize;
                var z = (coord.Z - job.ChunkMinZ) / job.ChunkGroupSize;
                if (x < 0 || x >= job.ChunkWidth || z < 0 || z >= job.ChunkHeight)
                {
                    continue;
                }

                var index = x + z * job.ChunkWidth;
                job.ChunkMapPixels[index] = new Color32(255, 255, 255, 255);
                changed = true;
            }

            while (job.ChunkQueue.TryDequeue(out var coord))
            {
                var x = (coord.X - job.ChunkMinX) / job.ChunkGroupSize;
                var z = (coord.Z - job.ChunkMinZ) / job.ChunkGroupSize;
                if (x < 0 || x >= job.ChunkWidth || z < 0 || z >= job.ChunkHeight)
                {
                    continue;
                }

                var index = x + z * job.ChunkWidth;
                job.ChunkMapPixels[index] = new Color32(46, 168, 50, 255);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            job.ChunkMapTexture.SetPixels32(job.ChunkMapPixels);
            job.ChunkMapTexture.Apply(false);
        }
    }
}
