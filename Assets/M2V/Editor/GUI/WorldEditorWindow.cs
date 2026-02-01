using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Bakery.Meshing;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow : EditorWindow
    {
        private const string UxmlPath = "Assets/M2V/Editor/GUI/WorldFolderSelect.uxml";
        private const string UssPath = "Assets/M2V/Editor/GUI/WorldFolderSelect.uss";
        private const string OverworldIconPath = "Assets/M2V/Editor/GUI/Overworld.png";
        private const string NetherIconPath = "Assets/M2V/Editor/GUI/Nether.png";
        private const string EndIconPath = "Assets/M2V/Editor/GUI/End.png";

        private Label _statusLabel;
        private ListView _worldList;
        private IntegerField _minXField;
        private IntegerField _minYField;
        private IntegerField _minZField;
        private IntegerField _maxXField;
        private IntegerField _maxYField;
        private IntegerField _maxZField;
        private VisualElement _rootElement;
        private DropdownField _languageDropdown;
        private VisualElement _languageHost;
        private Label _topbarUser;
        private Label _topbarHelp;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _worldsTitle;
        private Label _worldTipLabel;
        private Label _rangeTitle;
        private Label _rangeHint;
        private Label _rangeMinLabel;
        private Label _rangeMaxLabel;
        private Label _rangeDimensionLabel;
        private Label _dimensionOverworldLabel;
        private Label _dimensionNetherLabel;
        private Label _dimensionEndLabel;
        private Label _generateTitle;
        private Label _generateHint;
        private Label _playCaption;
        private Label _summaryWorld;
        private Label _summaryRange;
        private Label _summaryDimension;
        private Label _summaryPacks;
        private Label _loadingTitle;
        private Button _dimensionOverworldButton;
        private Button _dimensionNetherButton;
        private Button _dimensionEndButton;
        private Image _dimensionOverworldIcon;
        private Image _dimensionNetherIcon;
        private Image _dimensionEndIcon;
        private Button _openButton;
        private Button _meshButton;
        private Button _customImportButton;
        private Button _reloadButton;
        private Button _clearButton;
        private VisualElement _loadingOverlay;
        private VisualElement _loadingBar;
        private IVisualElementScheduledItem _loadingAnimation;
        private Label _loadingStatusLabel;
        private Button _nextWorldButton;
        private Button _nextRangeButton;
        private Button _backRangeButton;
        private Button _backGenerateButton;
        private VisualElement _pageWorld;
        private VisualElement _pageRange;
        private VisualElement _pageGenerate;
        private Label _stepWorld;
        private Label _stepRange;
        private Label _stepGenerate;
        private int _currentPageIndex;
        private IVisualElementScheduledItem _pageAnimation;

        private readonly EditorState _state = new EditorState();
        private static bool s_logChunkDatOnce;
        private bool _isSyncingRange;

        [MenuItem("Tools/Minecraft2VRChat")]
        public static void Open()
        {
            var window = GetWindow<WorldEditorWindow>("Minecraft2VRChat");
            window.Show();
        }

        private void OnEnable()
        {
            EnsureServices();
            BuildUi();
        }

        public void CreateGUI()
        {
            EnsureServices();
            BuildUi();
        }

        private void EnsureServices()
        {
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
                rootVisualElement.Add(new Label("Missing UI layout: WorldFolderSelect.uxml"));
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

    }
}
