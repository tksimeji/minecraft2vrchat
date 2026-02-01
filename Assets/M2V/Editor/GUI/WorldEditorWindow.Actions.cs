using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Bakery;
using M2V.Editor.Bakery.Meshing;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
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
            ShowLoadingOverlay();
            rootVisualElement.schedule.Execute(() =>
            {
                GenerateMeshInternal();
                HideLoadingOverlay();
            }).ExecuteLater(1);
        }
        private void GenerateMeshInternal()
        {
            SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingReadingBlocks));
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

            SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingGeneratingMesh));
            var context = new BakeryContext
            {
                WorldFolder = path,
                MinecraftJarPath = jarPath,
                LevelStem = dimensionId,
                Min = min,
                Max = max,
                UseGreedy = false,
                ApplyCoordinateTransform = true,
                LogSliceStats = false,
                LogPaletteBounds = false,
                UseTextureAtlas = true,
                LogChunkOnce = s_logChunkDatOnce
            };
            using (var session = M2VBakery.Create(context, out var sessionMessage))
            {
                if (session == null)
                {
                    var dialogMessage = string.IsNullOrEmpty(sessionMessage)
                        ? Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed)
                        : sessionMessage;
                    Debug.LogWarning(dialogMessage);
                    EditorUtility.DisplayDialog(Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                        dialogMessage,
                        "OK");
                    return;
                }

                var result = session.Bake(context);
                s_logChunkDatOnce = result.LogChunkOnce;
                if (result.Mesh == null)
                {
                    var dialogMessage = string.IsNullOrEmpty(result.Message)
                        ? Localization.Get(_state.Language, Localization.Keys.DialogMeshFailed)
                        : result.Message;
                    Debug.LogWarning(dialogMessage);
                    EditorUtility.DisplayDialog(Localization.Get(_state.Language, Localization.Keys.DialogTitle),
                        dialogMessage,
                        "OK");
                    return;
                }

                SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingApplyingMaterial));
                var go = MeshInstaller.InstallMesh("WorldMesh", result.Mesh, result.AtlasTexture);

                Selection.activeObject = go;
                var modeLabel = context.UseGreedy ? "Greedy" : "Naive";
                Debug.Log($"[Minecraft2VRChat] {modeLabel} mesh generated. Vertices: {result.Mesh.vertexCount}");
            }
        }
    }
}
