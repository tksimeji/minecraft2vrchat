using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering;
using fNbt;
using Object = UnityEngine.Object;

namespace M2V.Editor
{
    public class M2VEditorWindow : EditorWindow
    { 
        private const string UxmlPath = "Assets/M2V/Editor/M2VWorldFolderSelect.uxml";
        private const string UssPath = "Assets/M2V/Editor/M2VWorldFolderSelect.uss";
        private const string DirtTexturePath = "Assets/M2V/Editor/dirt.png";
        private const string DoubleSidedShaderPath = "Assets/M2V/Editor/M2VUnlitDoubleSided.shader";
        private const string DoubleSidedTransparentShaderPath = "Assets/M2V/Editor/M2VUnlitDoubleSidedTransparent.shader";

        private Label _statusLabel;
        private ListView _worldList;
        private IntegerField _minXField;
        private IntegerField _minYField;
        private IntegerField _minZField;
        private IntegerField _maxXField;
        private IntegerField _maxYField;
        private IntegerField _maxZField;
        private DropdownField _dimensionDropdown;
        private Button _openButton;
        private Button _importButton;
        private Button _meshButton;
        private readonly List<WorldEntry> _worldEntries = new List<WorldEntry>();
        private readonly List<DimensionOption> _dimensionOptions = new List<DimensionOption>();
        private string _currentWorldPath = string.Empty;
        private string _selectedWorldPath = string.Empty;
        private static bool s_logChunkDatOnce;
        private static bool s_logLevelDatOnce;

        private class WorldEntry
        {
            public string Path;
            public string Name;
            public Texture2D Icon;
            public bool IsValid;
            public string FolderName;
            public string LastPlayed;
            public string GameMode;
            public string Version;
        }

        private class DimensionOption
        {
            public string Id;
            public string Label;
        }

        [MenuItem("Tools/Minecraft2VRChat")]
        public static void Open()
        {
            var window = GetWindow<M2VEditorWindow>("Minecraft2VRChat");
            window.Show();
        }

        private void OnEnable()
        {
            BuildUi();
        }

        public void CreateGUI()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);

            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }
            else
            {
                Debug.LogError($"Missing style sheet at {UssPath}");
            }

            if (visualTree == null)
            {
                Debug.LogError($"Missing UXML at {UxmlPath}");
                rootVisualElement.Add(new Label("Missing UI layout: M2VWorldFolderSelect.uxml"));
                return;
            }

            var container = visualTree.CloneTree();
            container.style.flexGrow = 1f;
            rootVisualElement.Add(container);
            WireUi();
        }

        private void OnGUI()
        {
            if (rootVisualElement.childCount == 0)
            {
                EditorGUILayout.HelpBox("UI failed to load. Check Console for missing assets.", MessageType.Error);
            }
        }

        private void WireUi()
        {
            _statusLabel = rootVisualElement.Q<Label>("statusLabel");
            _worldList = rootVisualElement.Q<ListView>("worldList");
            _minXField = rootVisualElement.Q<IntegerField>("minXField");
            _minYField = rootVisualElement.Q<IntegerField>("minYField");
            _minZField = rootVisualElement.Q<IntegerField>("minZField");
            _maxXField = rootVisualElement.Q<IntegerField>("maxXField");
            _maxYField = rootVisualElement.Q<IntegerField>("maxYField");
            _maxZField = rootVisualElement.Q<IntegerField>("maxZField");
            _dimensionDropdown = rootVisualElement.Q<DropdownField>("dimensionDropdown");

            _openButton = rootVisualElement.Q<Button>("openButton");
            var clearButton = rootVisualElement.Q<Button>("clearButton");
            _importButton = rootVisualElement.Q<Button>("importButton");
            _meshButton = rootVisualElement.Q<Button>("meshButton");
            var customImportButton = rootVisualElement.Q<Button>("customImportButton");
            var reloadButton = rootVisualElement.Q<Button>("reloadButton");

            if (_statusLabel == null || _worldList == null ||
                _minXField == null || _minYField == null || _minZField == null ||
                _maxXField == null || _maxYField == null || _maxZField == null || _dimensionDropdown == null ||
                _openButton == null || clearButton == null || _importButton == null || _meshButton == null || customImportButton == null || reloadButton == null)
            {
                return;
            }

            ConfigureWorldList();
            RefreshWorldList();
            SetDefaultRange();

            customImportButton.clicked += () =>
            {
                var startPath = string.IsNullOrEmpty(GetSelectedPath())
                    ? GetDefaultWorldsPath()
                    : GetSelectedPath();
                var selected = EditorUtility.OpenFolderPanel("Select Minecraft World Folder", startPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    if (!SelectWorldInList(selected))
                    {
                        _worldList.ClearSelection();
                        _selectedWorldPath = selected;
                    }
                    UpdateValidation();
                }
            };

            reloadButton.clicked += () =>
            {
                RefreshWorldList();
                _currentWorldPath = string.Empty;
                UpdateValidation();
            };

            _openButton.clicked += () =>
            {
                var path = GetSelectedPath();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    EditorUtility.RevealInFinder(path);
                }
            };

            clearButton.clicked += () =>
            {
                _worldList.ClearSelection();
                _selectedWorldPath = string.Empty;
                UpdateValidation();
            };

            _importButton.clicked += () =>
            {
                var path = GetSelectedPath();
                if (IsValidWorldFolder(path))
                {
                    s_logChunkDatOnce = true;
                    s_logLevelDatOnce = true;
                    var dimensionId = GetSelectedDimensionId();
                    if (!TryGetRange(out var min, out var max))
                    {
                        EditorUtility.DisplayDialog("Minecraft2VRChat", "Please enter valid range values.", "OK");
                        return;
                    }

                    if (IsDefaultRange(min, max))
                    {
                        ApplySpawnDefaultRange(path);
                        TryGetRange(out min, out max);
                    }

                    Debug.Log($"[Minecraft2VRChat] Using range Min({min.x},{min.y},{min.z}) Max({max.x},{max.y},{max.z}) Dimension: {dimensionId}");
                    LogLevelDatOnce(path);
                    var logChunkOnce = s_logChunkDatOnce;
                    var count = M2VMeshGenerator.CountBlocksInRange(path, dimensionId, min, max, ref logChunkOnce);
                    s_logChunkDatOnce = logChunkOnce;
                    Debug.Log($"[Minecraft2VRChat] Blocks in range ({min.x},{min.y},{min.z})-({max.x},{max.y},{max.z}) " +
                              $"Dimension: {dimensionId} Count: {count}");
                }
                else
                {
                    EditorUtility.DisplayDialog("Minecraft2VRChat", "Please select a valid Minecraft world folder.", "OK");
                }
            };

            _meshButton.clicked += () =>
            {
                var path = GetSelectedPath();
                if (!IsValidWorldFolder(path))
                {
                    EditorUtility.DisplayDialog("Minecraft2VRChat", "Please select a valid Minecraft world folder.", "OK");
                    return;
                }

                var dimensionId = GetSelectedDimensionId();
                if (!TryGetRange(out var min, out var max))
                {
                    EditorUtility.DisplayDialog("Minecraft2VRChat", "Please enter valid range values.", "OK");
                    return;
                }

                var versionName = TryGetWorldVersionName(path);
                var jarPath = GetMinecraftVersionJarPath(versionName);
                if (string.IsNullOrEmpty(jarPath))
                {
                    EditorUtility.DisplayDialog("Minecraft2VRChat", "Minecraft version jar not found for this world.", "OK");
                    return;
                }

                var options = new M2VMeshGenerator.Options
                {
                    UseGreedy = false,
                    ApplyCoordinateTransform = true,
                    LogSliceStats = false,
                    LogPaletteBounds = false,
                    UseTextureAtlas = true
                };
                var logChunkOnce = s_logChunkDatOnce;
                var mesh = M2VMeshGenerator.GenerateMesh(path, dimensionId, min, max, jarPath, options, ref logChunkOnce, out var message, out var atlasTexture);
                s_logChunkDatOnce = logChunkOnce;
                if (mesh == null)
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        Debug.LogWarning(message);
                    }

                    EditorUtility.DisplayDialog("Minecraft2VRChat", "Mesh generation failed or range too large.", "OK");
                    return;
                }
                
                var go = GameObject.Find("M2V_GreedyMesh");
                if (go == null)
                {
                    go = new GameObject("M2V_GreedyMesh");
                }

                var filter = go.GetComponent<MeshFilter>();
                if (filter == null)
                {
                    filter = go.AddComponent<MeshFilter>();
                }

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    renderer = go.AddComponent<MeshRenderer>();
                }

                filter.sharedMesh = mesh;
                ApplyAtlasMaterial(renderer, atlasTexture, mesh);

                Selection.activeObject = go;
                var modeLabel = options.UseGreedy ? "Greedy" : "Naive";
                Debug.Log($"[Minecraft2VRChat] {modeLabel} mesh generated. Vertices: {mesh.vertexCount}");
            };

            rootVisualElement.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (DragAndDrop.paths.Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.StopPropagation();
                }
            });

            rootVisualElement.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (DragAndDrop.paths.Length == 0)
                {
                    return;
                }

                var path = DragAndDrop.paths[0];
                if (Directory.Exists(path))
                {
                    if (!SelectWorldInList(path))
                    {
                        _worldList.ClearSelection();
                        _selectedWorldPath = path;
                    }
                    UpdateValidation();
                }

                evt.StopPropagation();
            });

            UpdateValidation();
        }

        private void ConfigureWorldList()
        {
            _worldList.selectionType = SelectionType.Single;
            _worldList.itemsSource = _worldEntries;
            _worldList.fixedItemHeight = 84f;
            _worldList.makeItem = () =>
            {
                var root = new VisualElement();
                root.AddToClassList("world-item");

                var icon = new Image();
                icon.AddToClassList("world-icon");
                root.Add(icon);

                var info = new VisualElement();
                info.AddToClassList("world-info");

                var name = new Label();
                name.AddToClassList("world-name");
                info.Add(name);

                var path = new Label();
                path.AddToClassList("world-path");
                info.Add(path);

                var meta = new Label();
                meta.AddToClassList("world-meta");
                info.Add(meta);

                root.Add(info);
                return root;
            };

            _worldList.bindItem = (element, index) =>
            {
                var entry = _worldEntries[index];
                var icon = element.Q<Image>();
                var name = element.Q<Label>(className: "world-name");
                var path = element.Q<Label>(className: "world-path");
                var meta = element.Q<Label>(className: "world-meta");

                icon.image = entry.Icon;
                name.text = string.IsNullOrEmpty(entry.Name) ? entry.FolderName : entry.Name;
                path.text = string.IsNullOrEmpty(entry.LastPlayed)
                    ? entry.FolderName
                    : $"{entry.FolderName} ({entry.LastPlayed})";
                meta.text = string.IsNullOrEmpty(entry.Version)
                    ? $"{entry.GameMode} Mode"
                    : $"{entry.GameMode} Mode, Version: {entry.Version}";
                element.tooltip = entry.Path;

                element.EnableInClassList("selected", _worldList.selectedIndex == index);
            };

            _worldList.onSelectionChange += selection =>
            {
                foreach (var item in selection)
                {
                    if (item is WorldEntry entry)
                    {
                        _selectedWorldPath = entry.Path;
                        ApplySpawnDefaultRange(entry.Path);
                        UpdateValidation();
                        break;
                    }
                }

                _worldList.Rebuild();
            };
        }

        private void RefreshWorldList()
        {
            _worldEntries.Clear();
            var savesPath = GetDefaultWorldsPath();
            if (Directory.Exists(savesPath))
            {
                foreach (var dir in Directory.GetDirectories(savesPath))
                {
                    var isValid = IsValidWorldFolder(dir);
                    if (!isValid)
                    {
                        continue;
                    }

                    var meta = TryGetWorldMeta(dir);
                    var icon = LoadWorldIcon(dir);
                    _worldEntries.Add(new WorldEntry
                    {
                        Path = dir,
                        Name = meta.Name,
                        Icon = icon,
                        IsValid = isValid,
                        FolderName = Path.GetFileName(dir),
                        LastPlayed = meta.LastPlayed,
                        GameMode = meta.GameMode,
                        Version = meta.Version
                    });
                }
            }

            _worldList.Rebuild();

            if (_worldEntries.Count > 0 && _worldList.selectedIndex < 0)
            {
                _worldList.SetSelection(0);
                _selectedWorldPath = _worldEntries[0].Path;
                _currentWorldPath = string.Empty;
                UpdateDimensionChoices(_selectedWorldPath);
                ApplySpawnDefaultRange(_selectedWorldPath);
            }
        }

        private bool SelectWorldInList(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            for (var i = 0; i < _worldEntries.Count; i++)
            {
                if (_worldEntries[i].Path == path)
                {
                    _worldList.SetSelection(i);
                    return true;
                }
            }

            return false;
        }

        private string GetSelectedPath()
        {
            return _selectedWorldPath ?? string.Empty;
        }

        private string GetSelectedDimensionId()
        {
            if (_dimensionDropdown == null || _dimensionOptions.Count == 0)
            {
                return "minecraft:overworld";
            }

            var label = _dimensionDropdown.value;
            foreach (var option in _dimensionOptions)
            {
                if (option.Label == label)
                {
                    return option.Id;
                }
            }

            return _dimensionOptions[0].Id;
        }

        private void SetDefaultRange()
        {
            _minXField.value = -10;
            _minYField.value = 60;
            _minZField.value = -10;
            _maxXField.value = 10;
            _maxYField.value = 90;
            _maxZField.value = 10;
        }

        private void ApplySpawnDefaultRange(string worldFolder)
        {
            var spawn = TryGetSpawnPosition(worldFolder);
            if (spawn == null)
            {
                Debug.Log("[Minecraft2VRChat] Spawn position not found. Keeping current range.");
                return;
            }

            var center = spawn.Value;
            _minXField.value = center.x - 10;
            _minYField.value = center.y - 10;
            _minZField.value = center.z - 10;
            _maxXField.value = center.x + 10;
            _maxYField.value = center.y + 20;
            _maxZField.value = center.z + 10;

            Debug.Log($"[Minecraft2VRChat] Spawn position: {center.x}, {center.y}, {center.z}. Range centered around spawn.");
        }

        private bool TryGetRange(out Vector3Int min, out Vector3Int max)
        {
            min = Vector3Int.zero;
            max = Vector3Int.zero;

            if (_minXField == null || _minYField == null || _minZField == null ||
                _maxXField == null || _maxYField == null || _maxZField == null)
            {
                return false;
            }

            var minX = _minXField.value;
            var minY = _minYField.value;
            var minZ = _minZField.value;
            var maxX = _maxXField.value;
            var maxY = _maxYField.value;
            var maxZ = _maxZField.value;

            min = new Vector3Int(Mathf.Min(minX, maxX), Mathf.Min(minY, maxY), Mathf.Min(minZ, maxZ));
            max = new Vector3Int(Mathf.Max(minX, maxX), Mathf.Max(minY, maxY), Mathf.Max(minZ, maxZ));
            return true;
        }

        private static bool IsDefaultRange(Vector3Int min, Vector3Int max)
        {
            return min.x == -10 && min.y == 60 && min.z == -10
                   && max.x == 10 && max.y == 90 && max.z == 10;
        }

        private void UpdateValidation()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var path = GetSelectedPath();
            var isValid = IsValidWorldFolder(path);
            _selectedWorldPath = path;
            if (_openButton != null)
            {
                _openButton.SetEnabled(!string.IsNullOrEmpty(path) && Directory.Exists(path));
            }

            if (_importButton != null)
            {
                _importButton.SetEnabled(isValid);
            }

            _statusLabel.RemoveFromClassList("ok");
            _statusLabel.RemoveFromClassList("error");

            if (string.IsNullOrEmpty(path))
            {
                _statusLabel.text = "No folder selected.";
                _statusLabel.AddToClassList("error");
                _currentWorldPath = string.Empty;
                UpdateDimensionChoices(string.Empty);
                return;
            }

            if (isValid)
            {
                _statusLabel.text = "World folder looks valid.";
                _statusLabel.AddToClassList("ok");
                if (_currentWorldPath != path)
                {
                    _currentWorldPath = path;
                    UpdateDimensionChoices(path);
                    ApplySpawnDefaultRange(path);
                }
            }
            else
            {
                _statusLabel.text = "Invalid folder. Missing level.dat.";
                _statusLabel.AddToClassList("error");
                _currentWorldPath = string.Empty;
                UpdateDimensionChoices(string.Empty);
            }
        }

        private static Texture2D LoadWorldIcon(string worldFolder)
        {
            var iconPath = Path.Combine(worldFolder, "icon.png");
            if (!File.Exists(iconPath))
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    Object.DestroyImmediate(texture);
                    return null;
                }

                texture.name = "WorldIcon";
                texture.filterMode = FilterMode.Point;
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetWorldName(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return string.Empty;
            }

            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                return string.Empty;
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                var root = nbtFile.RootTag;
                if (root == null)
                {
                    return string.Empty;
                }

                var dataTag = root["Data"] as NbtCompound;
                if (dataTag == null)
                {
                    return string.Empty;
                }

                var nameTag = dataTag["LevelName"] as NbtString;
                return nameTag != null ? nameTag.Value : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Vector3Int? TryGetSpawnPosition(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return null;
            }

            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                return null;
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                var root = nbtFile.RootTag;
                var dataTag = root?["Data"] as NbtCompound;
                if (dataTag == null)
                {
                    return null;
                }

                var spawnX = GetIntFromTag(dataTag["SpawnX"]);
                var spawnY = GetIntFromTag(dataTag["SpawnY"]);
                var spawnZ = GetIntFromTag(dataTag["SpawnZ"]);
                if (spawnX != null && spawnY != null && spawnZ != null)
                {
                    return new Vector3Int(spawnX.Value, spawnY.Value, spawnZ.Value);
                }

                var spawnTag = dataTag["spawn"] as NbtCompound;
                if (spawnTag != null)
                {
                    var posArray = spawnTag["pos"] as NbtIntArray;
                    if (posArray != null && posArray.Value != null && posArray.Value.Length >= 3)
                    {
                        return new Vector3Int(posArray.Value[0], posArray.Value[1], posArray.Value[2]);
                    }
                }

                var playerTag = dataTag["Player"] as NbtCompound;
                if (playerTag != null)
                {
                    var posList = playerTag["Pos"] as NbtList;
                    if (posList != null && posList.Count >= 3 &&
                        posList[0] is NbtDouble px && posList[1] is NbtDouble py && posList[2] is NbtDouble pz)
                    {
                        return new Vector3Int((int)px.Value, (int)py.Value, (int)pz.Value);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static int? GetIntFromTag(NbtTag tag)
        {
            switch (tag)
            {
                case NbtInt nbtInt:
                    return nbtInt.Value;
                case NbtShort nbtShort:
                    return nbtShort.Value;
                case NbtLong nbtLong:
                    return (int)nbtLong.Value;
                case NbtByte nbtByte:
                    return nbtByte.Value;
                default:
                    return null;
            }
        }

        private static (string Name, string LastPlayed, string GameMode, string Version) TryGetWorldMeta(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return (string.Empty, string.Empty, "Unknown", string.Empty);
            }

            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                return (string.Empty, string.Empty, "Unknown", string.Empty);
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                var root = nbtFile.RootTag;
                if (root == null)
                {
                    return (string.Empty, string.Empty, "Unknown", string.Empty);
                }

                var dataTag = root["Data"] as NbtCompound;
                if (dataTag == null)
                {
                    return (string.Empty, string.Empty, "Unknown", string.Empty);
                }

                var name = (dataTag["LevelName"] as NbtString)?.Value ?? string.Empty;
                var lastPlayed = FormatLastPlayed(dataTag["LastPlayed"]);
                var gameMode = FormatGameMode(dataTag["GameType"]);
                var version = FormatVersion(dataTag["Version"]);
                return (name, lastPlayed, gameMode, version);
            }
            catch
            {
                return (string.Empty, string.Empty, "Unknown", string.Empty);
            }
        }

        private void UpdateDimensionChoices(string worldFolder)
        {
            if (_dimensionDropdown == null)
            {
                return;
            }

            _dimensionOptions.Clear();
            var dimensions = GetDimensions(worldFolder);
            foreach (var id in dimensions)
            {
                _dimensionOptions.Add(new DimensionOption
                {
                    Id = id,
                    Label = GetDimensionLabel(id)
                });
            }

            if (_dimensionOptions.Count == 0)
            {
                _dimensionDropdown.choices = new List<string>();
                _dimensionDropdown.value = string.Empty;
                return;
            }

            var labels = new List<string>(_dimensionOptions.Count);
            foreach (var option in _dimensionOptions)
            {
                labels.Add(option.Label);
            }

            _dimensionDropdown.choices = labels;

            var defaultIndex = _dimensionOptions.FindIndex(option => option.Id == "minecraft:overworld");
            if (defaultIndex < 0)
            {
                defaultIndex = 0;
            }

            _dimensionDropdown.value = _dimensionOptions[defaultIndex].Label;
        }

        private static List<string> GetDimensions(string worldFolder)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(worldFolder))
            {
                result.Add("minecraft:overworld");
                return result;
            }

            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                result.Add("minecraft:overworld");
                return result;
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                var root = nbtFile.RootTag;
                var dataTag = root?["Data"] as NbtCompound;
                var worldGenSettings = dataTag?["WorldGenSettings"] as NbtCompound;
                var dimensions = worldGenSettings?["dimensions"] as NbtCompound;
                if (dimensions != null)
                {
                    foreach (var child in dimensions.Tags)
                    {
                        result.Add(child.Name);
                    }
                }
            }
            catch
            {
                // ignore
            }

            if (result.Count == 0)
            {
                result.Add("minecraft:overworld");
                if (Directory.Exists(Path.Combine(worldFolder, "DIM-1")))
                {
                    result.Add("minecraft:the_nether");
                }

                if (Directory.Exists(Path.Combine(worldFolder, "DIM1")))
                {
                    result.Add("minecraft:the_end");
                }

                var dimRoot = Path.Combine(worldFolder, "dimensions");
                if (Directory.Exists(dimRoot))
                {
                    foreach (var ns in Directory.GetDirectories(dimRoot))
                    {
                        var nsName = Path.GetFileName(ns);
                        foreach (var dim in Directory.GetDirectories(ns))
                        {
                            var dimName = Path.GetFileName(dim);
                            var id = $"{nsName}:{dimName}";
                            if (!result.Contains(id))
                            {
                                result.Add(id);
                            }
                        }
                    }
                }
            }

            return result;
        }

        private static string GetDimensionLabel(string id)
        {
            return id switch
            {
                "minecraft:overworld" => "Overworld",
                "minecraft:the_nether" => "Nether",
                "minecraft:the_end" => "End",
                _ => id
            };
        }

        private static void ApplyDirtMaterial(MeshRenderer renderer, Mesh mesh)
        {
            if (renderer == null)
            {
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(DirtTexturePath);
            if (texture == null)
            {
                if (renderer.sharedMaterial == null)
                {
                    renderer.sharedMaterial = new Material(Shader.Find("Standard"));
                }

                return;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            if (IsUsingScriptableRenderPipeline())
            {
                if (mesh != null && mesh.subMeshCount > 1)
                {
                    renderer.sharedMaterials = new[]
                    {
                        CreateCutoutMaterial(texture),
                        CreateTransparentMaterial(texture)
                    };
                }
                else
                {
                    renderer.sharedMaterial = CreateCutoutMaterial(texture);
                }
                return;
            }

            if (mesh != null && mesh.subMeshCount > 1)
            {
                var materials = renderer.sharedMaterials;
                if (materials.Length != 2 || materials[0] == null || materials[1] == null ||
                    materials[0].shader == null || materials[1].shader == null ||
                    !materials[0].shader.isSupported || !materials[1].shader.isSupported ||
                    materials[0].mainTexture != texture || materials[1].mainTexture != texture)
                {
                    var cutout = CreateCutoutMaterial(texture);
                    var transparent = CreateTransparentMaterial(texture);
                    renderer.sharedMaterials = new[] { cutout, transparent };
                }
            }
            else if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ||
                     !renderer.sharedMaterial.shader.isSupported || renderer.sharedMaterial.mainTexture != texture)
            {
                var material = CreateCutoutMaterial(texture);
                renderer.sharedMaterial = material;
            }
        }

        private static void ApplyAtlasMaterial(MeshRenderer renderer, Texture2D atlasTexture, Mesh mesh)
        {
            if (atlasTexture == null)
            {
                ApplyDirtMaterial(renderer, mesh);
                return;
            }

            atlasTexture.filterMode = FilterMode.Point;
            atlasTexture.wrapMode = TextureWrapMode.Repeat;
            if (IsUsingScriptableRenderPipeline())
            {
                if (mesh != null && mesh.subMeshCount > 1)
                {
                    renderer.sharedMaterials = new[]
                    {
                        CreateCutoutMaterial(atlasTexture),
                        CreateTransparentMaterial(atlasTexture)
                    };
                }
                else
                {
                    renderer.sharedMaterial = CreateCutoutMaterial(atlasTexture);
                }
                return;
            }

            if (mesh != null && mesh.subMeshCount > 1)
            {
                var materials = renderer.sharedMaterials;
                if (materials.Length != 2 || materials[0] == null || materials[1] == null ||
                    materials[0].shader == null || materials[1].shader == null ||
                    !materials[0].shader.isSupported || !materials[1].shader.isSupported ||
                    materials[0].mainTexture != atlasTexture || materials[1].mainTexture != atlasTexture)
                {
                    var cutoutMaterial = CreateCutoutMaterial(atlasTexture);
                    var transparentMaterial = CreateTransparentMaterial(atlasTexture);
                    renderer.sharedMaterials = new[] { cutoutMaterial, transparentMaterial };
                }
            }
            else if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ||
                     !renderer.sharedMaterial.shader.isSupported || renderer.sharedMaterial.mainTexture != atlasTexture)
            {
                var cutoutMaterial = CreateCutoutMaterial(atlasTexture);
                renderer.sharedMaterial = cutoutMaterial;
            }
        }

        private static void LogLevelDatOnce(string worldFolder)
        {
            if (!s_logLevelDatOnce)
            {
                return;
            }

            s_logLevelDatOnce = false;
            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                Debug.LogWarning("[Minecraft2VRChat] level.dat not found for debug logging.");
                return;
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                Debug.Log($"[Minecraft2VRChat] level.dat NBT:\n{nbtFile.RootTag}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Minecraft2VRChat] Failed to log level.dat: {ex.Message}");
            }
        }

        private static string FormatLastPlayed(NbtTag tag)
        {
            if (tag is not NbtLong longTag)
            {
                return string.Empty;
            }

            var value = longTag.Value;
            if (value <= 0)
            {
                return string.Empty;
            }

            try
            {
                DateTimeOffset time;
                if (value > 1_000_000_000_000)
                {
                    time = DateTimeOffset.FromUnixTimeMilliseconds(value);
                }
                else if (value > 1_000_000_000)
                {
                    time = DateTimeOffset.FromUnixTimeSeconds(value);
                }
                else
                {
                    return string.Empty;
                }

                return time.LocalDateTime.ToString("M/d/yy, h:mm tt");
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatGameMode(NbtTag tag)
        {
            if (tag is not NbtInt intTag)
            {
                return "Unknown";
            }

            return intTag.Value switch
            {
                0 => "Survival",
                1 => "Creative",
                2 => "Adventure",
                3 => "Spectator",
                _ => "Unknown"
            };
        }

        private static string FormatVersion(NbtTag tag)
        {
            if (tag is not NbtCompound compound)
            {
                return string.Empty;
            }

            var name = (compound["Name"] as NbtString)?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            if (compound["Id"] is NbtInt idTag)
            {
                return idTag.Value.ToString();
            }

            return string.Empty;
        }

        private static string TryGetWorldVersionName(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return string.Empty;
            }

            var levelDatPath = Path.Combine(worldFolder, "level.dat");
            if (!File.Exists(levelDatPath))
            {
                return string.Empty;
            }

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromFile(levelDatPath);
                var dataTag = nbtFile.RootTag?["Data"] as NbtCompound;
                var versionTag = dataTag?["Version"] as NbtCompound;
                var name = (versionTag?["Name"] as NbtString)?.Value;
                return name ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetMinecraftVersionJarPath(string versionName)
        {
            if (string.IsNullOrEmpty(versionName))
            {
                return string.Empty;
            }

            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return string.Empty;
            }

            var versionsRoot = Path.Combine(home, "Library", "Application Support", "minecraft", "versions");
            var jarPath = Path.Combine(versionsRoot, versionName, $"{versionName}.jar");
            return File.Exists(jarPath) ? jarPath : string.Empty;
        }

        private static Shader GetDoubleSidedShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DoubleSidedShaderPath);
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("M2V/UnlitDoubleSided");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            return FindSupportedShader("Unlit/Texture", "Universal Render Pipeline/Unlit", "HDRP/Unlit");
        }

        private static Shader GetDoubleSidedTransparentShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DoubleSidedTransparentShaderPath);
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            shader = Shader.Find("M2V/UnlitDoubleSidedTransparent");
            if (shader != null && shader.isSupported)
            {
                return shader;
            }

            return FindSupportedShader("Unlit/Transparent", "Universal Render Pipeline/Unlit", "HDRP/Unlit");
        }

        private static Shader FindSupportedShader(params string[] names)
        {
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader != null && shader.isSupported)
                {
                    return shader;
                }
            }

            return null;
        }

        private static bool IsUsingScriptableRenderPipeline()
        {
            return GraphicsSettings.currentRenderPipeline != null;
        }

        private static Material CreateCutoutMaterial(Texture2D texture)
        {
            if (IsUsingScriptableRenderPipeline())
            {
                return CreateUrpUnlitMaterial(texture, transparent: false);
            }

            return new Material(GetDoubleSidedShader()) { mainTexture = texture };
        }

        private static Material CreateTransparentMaterial(Texture2D texture)
        {
            if (IsUsingScriptableRenderPipeline())
            {
                return CreateUrpUnlitMaterial(texture, transparent: true);
            }

            return new Material(GetDoubleSidedTransparentShader()) { mainTexture = texture };
        }

        private static Material CreateUrpUnlitMaterial(Texture2D texture, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("HDRP/Unlit");
            if (shader == null)
            {
                return new Material(GetDoubleSidedShader()) { mainTexture = texture };
            }

            var material = new Material(shader) { mainTexture = texture };
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)CullMode.Off);
            }

            if (transparent)
            {
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 1f);
                }
                if (material.HasProperty("_AlphaClip"))
                {
                    material.SetFloat("_AlphaClip", 0f);
                }
                if (material.HasProperty("_ZWrite"))
                {
                    material.SetFloat("_ZWrite", 0f);
                }
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 0f);
                }
                if (material.HasProperty("_AlphaClip"))
                {
                    material.SetFloat("_AlphaClip", 1f);
                }
                if (material.HasProperty("_Cutoff"))
                {
                    material.SetFloat("_Cutoff", 0.5f);
                }
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }

            return material;
        }

        private static bool IsValidWorldFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return false;
            }

            var levelDatPath = Path.Combine(path, "level.dat");
            return File.Exists(levelDatPath);
        }

        private static string GetDefaultWorldsPath()
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return string.Empty;
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
                return string.IsNullOrEmpty(appData)
                    ? string.Empty
                    : Path.Combine(appData, ".minecraft", "saves");
            }

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return Path.Combine(home, "Library", "Application Support", "minecraft", "saves");
            }

            return Path.Combine(home, ".minecraft", "saves");
        }
    }
}
