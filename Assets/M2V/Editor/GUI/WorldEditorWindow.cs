#nullable enable

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow : EditorWindow
    {
        private const string UxmlPath = "Assets/M2V/Editor/GUI/WorldFolderSelect.uxml";
        private const string UssPath = "Assets/M2V/Editor/GUI/WorldFolderSelect.uss";
        private const string OverworldIconPath = "Assets/M2V/Editor/GUI/Overworld.png";
        private const string NetherIconPath = "Assets/M2V/Editor/GUI/Nether.png";
        private const string EndIconPath = "Assets/M2V/Editor/GUI/End.png";

        private Label _statusLabel = null!;
        private ListView _worldList = null!;
        private IntegerField _minXField = null!;
        private IntegerField _minYField = null!;
        private IntegerField _minZField = null!;
        private IntegerField _maxXField = null!;
        private IntegerField _maxYField = null!;
        private IntegerField _maxZField = null!;
        private VisualElement _rootElement = null!;
        private DropdownField _languageDropdown = null!;
        private VisualElement _languageHost = null!;
        private Label _titleLabel = null!;
        private Label _subtitleLabel = null!;
        private Label _worldsTitle = null!;
        private Label _worldTipLabel = null!;
        private Label _rangeTitle = null!;
        private Label _rangeHint = null!;
        private Label _rangeMinLabel = null!;
        private Label _rangeMaxLabel = null!;
        private Label _rangeDimensionLabel = null!;
        private Label _blockScaleLabel = null!;
        private Label _dimensionOverworldLabel = null!;
        private Label _dimensionNetherLabel = null!;
        private Label _dimensionEndLabel = null!;
        private Label _generateTitle = null!;
        private Label _generateHint = null!;
        private Label _playCaption = null!;
        private Label _summaryWorld = null!;
        private Label _summaryRange = null!;
        private Label _summaryDimension = null!;
        private Label _summaryScale = null!;
        private Label _summaryPacks = null!;
        private Label _loadingTitle = null!;
        private Button _cancelButton = null!;
        private Button _dimensionOverworldButton = null!;
        private Button _dimensionNetherButton = null!;
        private Button _dimensionEndButton = null!;
        private Image _dimensionOverworldIcon = null!;
        private Image _dimensionNetherIcon = null!;
        private Image _dimensionEndIcon = null!;
        private Button _openButton = null!;
        private Button _meshButton = null!;
        private Button _customImportButton = null!;
        private Button _reloadButton = null!;
        private Button _clearButton = null!;
        private VisualElement _loadingOverlay = null!;
        private VisualElement _loadingBar = null!;
        private IVisualElementScheduledItem? _loadingAnimation;
        private Label _loadingStatusLabel = null!;
        private Image _loadingMap = null!;
        private Button _nextWorldButton = null!;
        private Button _nextRangeButton = null!;
        private Button _backRangeButton = null!;
        private Button _backGenerateButton = null!;
        private VisualElement _pageWorld = null!;
        private VisualElement _pageRange = null!;
        private VisualElement _pageGenerate = null!;
        private Label _stepWorld = null!;
        private Label _stepRange = null!;
        private Label _stepGenerate = null!;
        private int _currentPageIndex;
        private IVisualElementScheduledItem? _pageAnimation;

        private readonly EditorState _state = new EditorState();
        private static bool s_logChunkDatOnce;
        private bool _isSyncingRange;
        private FloatField _blockScaleField = null!;

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
        private void OnDisable()
        {
            CancelMeshing();
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