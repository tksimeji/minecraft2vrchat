using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace M2V.Editor
{
    public class M2VEditorWindow : EditorWindow
    { 
        private const string UxmlPath = "Assets/M2V/Editor/M2VWorldFolderSelect.uxml";
        private const string UssPath = "Assets/M2V/Editor/M2VWorldFolderSelect.uss";

        private Label _worldPathLabel;
        private Label _statusLabel;
        private Button _openButton;
        private Button _importButton;

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
            _worldPathLabel = rootVisualElement.Q<Label>("worldPathLabel");
            _statusLabel = rootVisualElement.Q<Label>("statusLabel");

            var browseButton = rootVisualElement.Q<Button>("browseButton");
            _openButton = rootVisualElement.Q<Button>("openButton");
            var clearButton = rootVisualElement.Q<Button>("clearButton");
            _importButton = rootVisualElement.Q<Button>("importButton");

            if (_worldPathLabel == null || _statusLabel == null ||
                browseButton == null || _openButton == null || clearButton == null || _importButton == null)
            {
                return;
            }

            _worldPathLabel.text = GetDefaultWorldsPath();

            browseButton.clicked += () =>
            {
                var startPath = string.IsNullOrEmpty(_worldPathLabel.text) || _worldPathLabel.text == "No folder selected."
                    ? GetDefaultWorldsPath()
                    : _worldPathLabel.text;
                var selected = EditorUtility.OpenFolderPanel("Select Minecraft World Folder", startPath, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    _worldPathLabel.text = selected;
                    UpdateValidation();
                }
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
                _worldPathLabel.text = "No folder selected.";
                UpdateValidation();
            };

            _importButton.clicked += () =>
            {
                var path = GetSelectedPath();
                if (IsValidWorldFolder(path))
                {
                    EditorUtility.DisplayDialog("Minecraft2VRChat", "World folder selected.", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Minecraft2VRChat", "Please select a valid Minecraft world folder.", "OK");
                }
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
                    _worldPathLabel.text = path;
                    UpdateValidation();
                }

                evt.StopPropagation();
            });

            UpdateValidation();
        }

        private string GetSelectedPath()
        {
            if (_worldPathLabel == null)
            {
                return string.Empty;
            }

            return _worldPathLabel.text == "No folder selected."
                ? string.Empty
                : _worldPathLabel.text;
        }

        private void UpdateValidation()
        {
            if (_worldPathLabel == null || _statusLabel == null)
            {
                return;
            }

            var path = GetSelectedPath();
            var isValid = IsValidWorldFolder(path);
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
                return;
            }

            if (isValid)
            {
                _statusLabel.text = "World folder looks valid.";
                _statusLabel.AddToClassList("ok");
            }
            else
            {
                _statusLabel.text = "Invalid folder. Missing level.dat.";
                _statusLabel.AddToClassList("error");
            }
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
