using System;
using System.Collections.Generic;
using System.IO;
using fNbt;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor
{
    public class M2VEditorWindow : EditorWindow
    {
        private const string UxmlPath = "Assets/M2V/Editor/UI/M2VWorldFolderSelect.uxml";
        private const string UssPath = "Assets/M2V/Editor/UI/M2VWorldFolderSelect.uss";
        private const string DirtTexturePath = "Assets/M2V/Editor/UI/dirt.png";
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
        private RadioButton _dimensionOverworld;
        private RadioButton _dimensionNether;
        private RadioButton _dimensionEnd;
        private Button _openButton;
        private Button _importButton;
        private Button _meshButton;

        private readonly M2VEditorState _state = new M2VEditorState();
        private static bool s_logChunkDatOnce;
        private static bool s_logLevelDatOnce;
        private bool _isSyncingRange;

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
            _dimensionOverworld = rootVisualElement.Q<RadioButton>("dimensionOverworld");
            _dimensionNether = rootVisualElement.Q<RadioButton>("dimensionNether");
            _dimensionEnd = rootVisualElement.Q<RadioButton>("dimensionEnd");

            _openButton = rootVisualElement.Q<Button>("openButton");
            var clearButton = rootVisualElement.Q<Button>("clearButton");
            _importButton = rootVisualElement.Q<Button>("importButton");
            _meshButton = rootVisualElement.Q<Button>("meshButton");
            var customImportButton = rootVisualElement.Q<Button>("customImportButton");
            var reloadButton = rootVisualElement.Q<Button>("reloadButton");

            if (!IsUiReady(clearButton, customImportButton, reloadButton))
            {
                return;
            }

            ConfigureWorldList();
            RefreshWorldList();
            _state.SetDefaultRange();
            SyncStateToUi();
            RegisterRangeFieldHandlers();

            customImportButton.clicked += OnClickCustomImport;
            reloadButton.clicked += OnClickReload;
            _openButton.clicked += OnClickOpen;
            clearButton.clicked += OnClickClear;
            _importButton.clicked += OnClickImport;
            _meshButton.clicked += OnClickGenerateMesh;

            RegisterDragHandlers();
            UpdateValidation();
        }

        private bool IsUiReady(Button clearButton, Button customImportButton, Button reloadButton)
        {
            return _statusLabel != null && _worldList != null &&
                   _minXField != null && _minYField != null && _minZField != null &&
                   _maxXField != null && _maxYField != null && _maxZField != null &&
                   _dimensionOverworld != null && _dimensionNether != null && _dimensionEnd != null &&
                   _openButton != null && clearButton != null && _importButton != null && _meshButton != null &&
                   customImportButton != null && reloadButton != null;
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

        private void OnClickImport()
        {
            var path = _state.GetSelectedPath();
            var worldDir = _state.GetSelectedWorld();
            if (worldDir == null)
            {
                EditorUtility.DisplayDialog("Minecraft2VRChat", "Please select a valid Minecraft world folder.", "OK");
                return;
            }

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
                ApplySpawnDefaultRange(worldDir);
                TryGetRange(out min, out max);
            }

            Debug.Log($"[Minecraft2VRChat] Using range Min({min.x},{min.y},{min.z}) Max({max.x},{max.y},{max.z}) Dimension: {dimensionId}");
            LogLevelDatOnce(worldDir);
            var logChunkOnce = s_logChunkDatOnce;
            var count = M2VMeshGenerator.CountBlocksInRange(path, dimensionId, min, max, ref logChunkOnce);
            s_logChunkDatOnce = logChunkOnce;
            Debug.Log($"[Minecraft2VRChat] Blocks in range ({min.x},{min.y},{min.z})-({max.x},{max.y},{max.z}) " +
                      $"Dimension: {dimensionId} Count: {count}");
        }

        private void OnClickGenerateMesh()
        {
            var path = _state.GetSelectedPath();
            var worldDir = _state.GetSelectedWorld();
            if (worldDir == null)
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

            var versionName = worldDir.VersionName;
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
        }

        private void RegisterDragHandlers()
        {
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
                    var dirInfo = new DirectoryInfo(path);
                    if (!SelectWorldInList(dirInfo))
                    {
                        _worldList.ClearSelection();
                        _state.SetSelectedWorld(dirInfo);
                    }
                    UpdateValidation();
                }

                evt.StopPropagation();
            });
        }

        private void RegisterRangeFieldHandlers()
        {
            _minXField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _minYField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _minZField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _maxXField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _maxYField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
            _maxZField.RegisterValueChangedCallback(evt => UpdateRangeFromUi());
        }

        private void ConfigureWorldList()
        {
            _worldList.selectionType = SelectionType.Single;
            _worldList.itemsSource = _state.WorldEntries;
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
                var entry = _state.WorldEntries[index];
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
                element.tooltip = entry.Path?.FullName ?? string.Empty;

                element.EnableInClassList("selected", _worldList.selectedIndex == index);
            };

            _worldList.onSelectionChange += selection =>
            {
                foreach (var item in selection)
                {
                    if (item is M2VEditorState.WorldEntry entry)
                    {
                        _state.SetSelectedWorld(entry.Path);
                        ApplySpawnDefaultRange(ResolveWorld(entry.Path));
                        UpdateValidation();
                        break;
                    }
                }

                _worldList.Rebuild();
            };
        }

        private void RefreshWorldList()
        {
            var savesPath = GetDefaultWorldsPath();
            _state.PopulateWorldEntries(savesPath, IsValidWorldFolder, TryGetWorldMeta, LoadWorldIcon);

            _worldList.Rebuild();

            if (_worldList.selectedIndex < 0 && _state.EnsureDefaultSelection())
            {
                _worldList.SetSelection(0);
                _state.SetCurrentWorld(null);
                UpdateDimensionChoices(_state.GetSelectedPath());
                ApplySpawnDefaultRange(_state.GetSelectedWorld());
            }
        }

        private bool SelectWorldInList(DirectoryInfo path)
        {
            if (_state.TryFindWorldIndex(path, out var index))
            {
                _worldList.SetSelection(index);
                return true;
            }

            return false;
        }

        private string GetSelectedDimensionId()
        {
            if (_dimensionOverworld == null || _dimensionNether == null || _dimensionEnd == null)
            {
                return "minecraft:overworld";
            }

            if (_dimensionNether.value)
            {
                return World.World.NetherId;
            }

            if (_dimensionEnd.value)
            {
                return World.World.EndId;
            }

            return World.World.OverworldId;
        }

        private void ApplySpawnDefaultRange(World.World worldDir)
        {
            var spawn = worldDir?.SpawnPosition;
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

        private void UpdateValidation()
        {
            if (_statusLabel == null)
            {
                return;
            }

            var path = _state.GetSelectedPath();
            var worldDir = _state.GetSelectedWorld();
            var isValid = _state.IsSelectedWorldValid();
            _state.SetSelectedWorld(string.IsNullOrEmpty(path) ? null : new DirectoryInfo(path));
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
                _state.SetCurrentWorld(null);
                UpdateDimensionChoices(string.Empty);
                return;
            }

            if (isValid)
            {
                _statusLabel.text = "World folder looks valid.";
                _statusLabel.AddToClassList("ok");
                if (!_state.IsSameAsCurrent(path))
                {
                    _state.SetCurrentWorld(new DirectoryInfo(path));
                    UpdateDimensionChoices(path);
                    ApplySpawnDefaultRange(worldDir);
                }
            }
            else
            {
                _statusLabel.text = "Invalid folder. Missing level.dat.";
                _statusLabel.AddToClassList("error");
                _state.SetCurrentWorld(null);
                UpdateDimensionChoices(string.Empty);
            }
        }

        private void UpdateDimensionChoices(string worldFolder)
        {
            if (_dimensionOverworld == null || _dimensionNether == null || _dimensionEnd == null)
            {
                return;
            }

            _dimensionOverworld.value = true;
            _dimensionNether.value = false;
            _dimensionEnd.value = false;
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

        private static M2VEditorState.WorldMeta TryGetWorldMeta(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
            {
                return M2VEditorState.WorldMeta.Empty;
            }

            var worldDir = ResolveWorld(string.IsNullOrEmpty(worldFolder) ? null : new DirectoryInfo(worldFolder));
            if (worldDir == null)
            {
                return M2VEditorState.WorldMeta.Empty;
            }

            return new M2VEditorState.WorldMeta(
                worldDir.LevelName,
                FormatLastPlayed(worldDir.LastPlayedTime),
                FormatGameMode(worldDir.GameType),
                worldDir.VersionName);
        }

        private static string FormatLastPlayed(long value)
        {
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

        private static string FormatGameMode(int gameType)
        {
            return gameType switch
            {
                0 => "Survival",
                1 => "Creative",
                2 => "Adventure",
                3 => "Spectator",
                _ => "Unknown"
            };
        }

        private static void LogLevelDatOnce(World.World worldDir)
        {
            if (!s_logLevelDatOnce)
            {
                return;
            }

            s_logLevelDatOnce = false;
            if (worldDir == null)
            {
                Debug.LogWarning("[Minecraft2VRChat] level.dat not found for debug logging.");
                return;
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

            return World.World.Of(new DirectoryInfo(path)) != null;
        }

        private static World.World ResolveWorld(DirectoryInfo dir)
        {
            return dir == null ? null : World.World.Of(dir);
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
    }
}
