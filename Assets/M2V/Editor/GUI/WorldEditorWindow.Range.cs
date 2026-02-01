using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Bakery.Meshing;
using M2V.Editor.Minecraft.World;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private void RegisterRangeFieldHandlers()
        {
            _minXField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _minYField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _minZField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _maxXField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _maxYField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _maxZField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
        }
        private LevelStem GetSelectedDimensionId()
        {
            return _state.SelectedLevelStem;
        }
        private void ApplySpawnDefaultRange(World worldDir)
        {
            var spawn = worldDir?.SpawnPos;
            if (spawn == null)
            {
                Debug.Log("[Minecraft2VRChat] Spawn position not found. Keeping current range.");
                return;
            }

            _state.SetRangeFromSpawn(spawn.Value);
            SyncStateToUi();

            var center = spawn.Value;
            Debug.Log($"[Minecraft2VRChat] Spawn position: {center.x}, {center.y}, {center.z}. Range centered around spawn.");
        }
        private bool TryGetRange(out Vector3Int min, out Vector3Int max)
        {
            if (_minXField == null || _minYField == null || _minZField == null ||
                _maxXField == null || _maxYField == null || _maxZField == null)
            {
                min = Vector3Int.zero;
                max = Vector3Int.zero;
                return false;
            }
            UpdateRangeFromUi();
            _state.GetRange(out min, out max);
            return true;
        }
        private void UpdateDimensionChoices(string worldFolder)
        {
            if (_dimensionOverworldButton == null || _dimensionNetherButton == null || _dimensionEndButton == null)
            {
                return;
            }

            _state.SetSelectedDimension(LevelStem.Overworld);
            UpdateDimensionSelection();
        }
        private void ConfigureDimensionIcons()
        {
            if (_dimensionOverworldButton == null || _dimensionNetherButton == null || _dimensionEndButton == null)
            {
                return;
            }

            _dimensionOverworldButton.clicked += () => SelectDimension(LevelStem.Overworld);
            _dimensionNetherButton.clicked += () => SelectDimension(LevelStem.Nether);
            _dimensionEndButton.clicked += () => SelectDimension(LevelStem.End);

            if (_dimensionOverworldIcon != null)
            {
                AssignUiIcon(_dimensionOverworldIcon, OverworldIconPath);
            }

            if (_dimensionNetherIcon != null)
            {
                AssignUiIcon(_dimensionNetherIcon, NetherIconPath);
            }

            if (_dimensionEndIcon != null)
            {
                AssignUiIcon(_dimensionEndIcon, EndIconPath);
            }

            UpdateDimensionSelection();
        }
        private void SelectDimension(LevelStem levelStemKey)
        {
            _state.SetSelectedDimension(levelStemKey);
            UpdateDimensionSelection();
            UpdateSummary();
        }
        private void UpdateDimensionSelection()
        {
            if (_dimensionOverworldButton == null || _dimensionNetherButton == null || _dimensionEndButton == null)
            {
                return;
            }

            var id = _state.SelectedLevelStem;
            _dimensionOverworldButton.EnableInClassList("selected", id == LevelStem.Overworld);
            _dimensionNetherButton.EnableInClassList("selected", id == LevelStem.Nether);
            _dimensionEndButton.EnableInClassList("selected", id == LevelStem.End);
        }
        private static void AssignUiIcon(Image target, string path)
        {
            if (target == null || string.IsNullOrEmpty(path))
            {
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                var changed = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (importer.alphaSource != TextureImporterAlphaSource.FromInput)
                {
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    changed = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    changed = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null && sprite.texture != null)
            {
                var texture = sprite.texture;
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                target.image = texture;
                target.scaleMode = ScaleMode.ScaleToFit;
                return;
            }

            var fallback = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (fallback != null)
            {
                fallback.filterMode = FilterMode.Point;
                fallback.wrapMode = TextureWrapMode.Clamp;
            }
            target.image = fallback;
            target.scaleMode = ScaleMode.ScaleToFit;
        }
        private static bool IsDefaultRange(Vector3Int min, Vector3Int max)
        {
            return min.x == -10 && min.y == 60 && min.z == -10
                   && max.x == 10 && max.y == 90 && max.z == 10;
        }
        private void UpdateRangeFromUi()
        {
            if (_isSyncingRange)
            {
                return;
            }

            _state.MinX = _minXField.value;
            _state.MinY = _minYField.value;
            _state.MinZ = _minZField.value;
            _state.MaxX = _maxXField.value;
            _state.MaxY = _maxYField.value;
            _state.MaxZ = _maxZField.value;
            UpdateRangeButtons();
            UpdateSummary();
        }
        private void SyncStateToUi()
        {
            if (_minXField == null || _minYField == null || _minZField == null ||
                _maxXField == null || _maxYField == null || _maxZField == null)
            {
                return;
            }

            _isSyncingRange = true;
            _minXField.value = _state.MinX;
            _minYField.value = _state.MinY;
            _minZField.value = _state.MinZ;
            _maxXField.value = _state.MaxX;
            _maxYField.value = _state.MaxY;
            _maxZField.value = _state.MaxZ;
            _isSyncingRange = false;
            UpdateRangeButtons();
            UpdateSummary();
        }
        private void UpdateRangeButtons()
        {
            if (_nextRangeButton == null)
            {
                return;
            }

            _nextRangeButton.SetEnabled(IsRangeValid() && _state.IsSelectedWorldValid());
        }
        private bool IsRangeValid()
        {
            _state.GetRange(out var min, out var max);
            var sizeX = max.x - min.x + 1;
            var sizeY = max.y - min.y + 1;
            var sizeZ = max.z - min.z + 1;
            return sizeX > 0 && sizeY > 0 && sizeZ > 0;
        }
        private string GetSelectedDimensionLabel(Language language)
        {
            return _state.SelectedLevelStem switch
            {
                LevelStem.Nether => Localization.Get(language, Localization.Keys.DimensionNether),
                LevelStem.End => Localization.Get(language, Localization.Keys.DimensionEnd),
                _ => Localization.Get(language, Localization.Keys.DimensionOverworld)
            };
        }
    }
}
