#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private void WireUi()
        {
            BindElements();
            if (!IsUiReady(_clearButton, _customImportButton, _reloadButton))
            {
                return;
            }

            ConfigureWorldList();
            RefreshWorldList();
            InitializeUiState();
            BindUiEvents();

            RegisterDragHandlers();
            UpdateValidation();
            SetPage(0);
        }

        private void BindElements()
        {
            _statusLabel = rootVisualElement.Q<Label>("statusLabel");
            _rootElement = rootVisualElement.Q<VisualElement>("root");
            _worldList = rootVisualElement.Q<ListView>("worldList");
            _minXField = rootVisualElement.Q<IntegerField>("minXField");
            _minYField = rootVisualElement.Q<IntegerField>("minYField");
            _minZField = rootVisualElement.Q<IntegerField>("minZField");
            _maxXField = rootVisualElement.Q<IntegerField>("maxXField");
            _maxYField = rootVisualElement.Q<IntegerField>("maxYField");
            _maxZField = rootVisualElement.Q<IntegerField>("maxZField");
            _languageDropdown = rootVisualElement.Q<DropdownField>("languageDropdown");
            _languageHost = rootVisualElement.Q<VisualElement>("languageHost");
            _titleLabel = rootVisualElement.Q<Label>("titleLabel");
            _subtitleLabel = rootVisualElement.Q<Label>("subtitleLabel");
            _worldsTitle = rootVisualElement.Q<Label>("worldsTitle");
            _worldTipLabel = rootVisualElement.Q<Label>("worldTipLabel");
            _rangeTitle = rootVisualElement.Q<Label>("rangeTitle");
            _rangeHint = rootVisualElement.Q<Label>("rangeHint");
            _rangeMinLabel = rootVisualElement.Q<Label>("rangeMinLabel");
            _rangeMaxLabel = rootVisualElement.Q<Label>("rangeMaxLabel");
            _rangeDimensionLabel = rootVisualElement.Q<Label>("rangeDimensionLabel");
            _blockScaleLabel = rootVisualElement.Q<Label>("blockScaleLabel");
            _rangeBlockCountLabel = rootVisualElement.Q<Label>("rangeBlockCountLabel");
            _blockScaleField = rootVisualElement.Q<FloatField>("blockScaleField");
            _dimensionOverworldLabel = rootVisualElement.Q<Label>("dimensionOverworldLabel");
            _dimensionNetherLabel = rootVisualElement.Q<Label>("dimensionNetherLabel");
            _dimensionEndLabel = rootVisualElement.Q<Label>("dimensionEndLabel");
            _generateTitle = rootVisualElement.Q<Label>("generateTitle");
            _generateHint = rootVisualElement.Q<Label>("generateHint");
            _playCaption = rootVisualElement.Q<Label>("playCaption");
            _summaryWorld = rootVisualElement.Q<Label>("summaryWorld");
            _summaryRange = rootVisualElement.Q<Label>("summaryRange");
            _summaryDimension = rootVisualElement.Q<Label>("summaryDimension");
            _summaryScale = rootVisualElement.Q<Label>("summaryScale");
            _summaryPacks = rootVisualElement.Q<Label>("summaryPacks");
            _loadingTitle = rootVisualElement.Q<Label>("loadingTitle");
            _dimensionOverworldButton = rootVisualElement.Q<Button>("dimensionOverworldButton");
            _dimensionNetherButton = rootVisualElement.Q<Button>("dimensionNetherButton");
            _dimensionEndButton = rootVisualElement.Q<Button>("dimensionEndButton");
            _dimensionOverworldIcon = rootVisualElement.Q<Image>("dimensionOverworldIcon");
            _dimensionNetherIcon = rootVisualElement.Q<Image>("dimensionNetherIcon");
            _dimensionEndIcon = rootVisualElement.Q<Image>("dimensionEndIcon");

            _openButton = rootVisualElement.Q<Button>("openButton");
            _clearButton = rootVisualElement.Q<Button>("clearButton");
            _meshButton = rootVisualElement.Q<Button>("meshButton");
            _loadingOverlay = rootVisualElement.Q<VisualElement>("loadingOverlay");
            _loadingBar = rootVisualElement.Q<VisualElement>("loadingBar");
            _loadingStatusLabel = rootVisualElement.Q<Label>("loadingStatusLabel");
            _loadingMap = rootVisualElement.Q<Image>("loadingMap");
            _cancelButton = rootVisualElement.Q<Button>("cancelButton");
            _discordButton = rootVisualElement.Q<Button>("discordButton");
            _nextWorldButton = rootVisualElement.Q<Button>("nextButtonWorld");
            _nextRangeButton = rootVisualElement.Q<Button>("nextButtonRange");
            _backRangeButton = rootVisualElement.Q<Button>("backButtonRange");
            _backGenerateButton = rootVisualElement.Q<Button>("backButtonGenerate");
            _customImportButton = rootVisualElement.Q<Button>("customImportButton");
            _reloadButton = rootVisualElement.Q<Button>("reloadButton");
            _pageWorld = rootVisualElement.Q<VisualElement>("pageWorld");
            _pageRange = rootVisualElement.Q<VisualElement>("pageRange");
            _pageGenerate = rootVisualElement.Q<VisualElement>("pageGenerate");
            _stepWorld = rootVisualElement.Q<Label>("stepWorld");
            _stepRange = rootVisualElement.Q<Label>("stepRange");
            _stepGenerate = rootVisualElement.Q<Label>("stepGenerate");
        }

        private void InitializeUiState()
        {
            _state.SetDefaultRange();
            SyncStateToUi();
            RegisterRangeFieldHandlers();
            ConfigureDimensionIcons();
            ConfigureLanguageDropdown();
            ApplyLocalization();
        }

        private void BindUiEvents()
        {
            _customImportButton.clicked += OnClickCustomImport;
            _reloadButton.clicked += OnClickReload;
            _openButton.clicked += OnClickOpen;
            _clearButton.clicked += OnClickClear;
            _meshButton.clicked += OnClickGenerateMesh;
            _cancelButton.clicked += CancelMeshing;
            if (_discordButton != null)
            {
                _discordButton.clicked += OnClickDiscord;
            }

            BindNavigation();
        }

        private void BindNavigation()
        {
            _currentPageIndex = 0;
            _nextWorldButton.clicked += () =>
            {
                Debug.Log("[M2V] Navigate: Worlds -> Range");
                SetPage(1);
            };

            _nextRangeButton.clicked += () =>
            {
                Debug.Log("[M2V] Navigate: Range -> Generate");
                SetPage(2);
            };

            _backRangeButton.clicked += () =>
            {
                Debug.Log("[M2V] Navigate: Range -> Worlds");
                SetPage(0);
            };

            _backGenerateButton.clicked += () =>
            {
                Debug.Log("[M2V] Navigate: Generate -> Range");
                SetPage(1);
            };

            _stepWorld.RegisterCallback<ClickEvent>(_ => SetPage(0));
            _stepRange.RegisterCallback<ClickEvent>(_ => SetPage(1));
            _stepGenerate.RegisterCallback<ClickEvent>(_ => SetPage(2));
        }

        private bool IsUiReady(Button? clearButton, Button? customImportButton, Button? reloadButton)
        {
            return clearButton != null && customImportButton != null && reloadButton != null;
        }

        private void SetPage(int pageIndex)
        {
            var previous = _currentPageIndex;
            _currentPageIndex = pageIndex;

            SetStepActive(_stepWorld, pageIndex == 0);
            SetStepActive(_stepRange, pageIndex == 1);
            SetStepActive(_stepGenerate, pageIndex == 2);

            if (previous != pageIndex)
            {
                AnimateTransition(previous, pageIndex);
                return;
            }

            SetPageActive(_pageWorld, pageIndex == 0);
            SetPageActive(_pageRange, pageIndex == 1);
            SetPageActive(_pageGenerate, pageIndex == 2);
        }

        private static void SetPageActive(VisualElement? page, bool active)
        {
            if (page == null)
            {
                return;
            }

            page.EnableInClassList("active", active);
            page.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void SetStepActive(Label? step, bool active)
        {
            if (step == null)
            {
                return;
            }

            step.EnableInClassList("m2v-tab--active", active);
        }

        private void AnimateTransition(int fromIndex, int toIndex)
        {
            var from = GetPageByIndex(fromIndex);
            var to = GetPageByIndex(toIndex);
            if (from == null || to == null)
            {
                return;
            }

            var width = rootVisualElement.resolvedStyle.width;
            if (width <= 0f)
            {
                return;
            }

            var direction = toIndex > fromIndex ? 1f : -1f;
            var containerWidth = Mathf.Max(width, 1f);
            from.style.display = DisplayStyle.Flex;
            to.style.display = DisplayStyle.Flex;
            from.style.left = new Length(0f, LengthUnit.Pixel);
            to.style.left = new Length(direction * containerWidth, LengthUnit.Pixel);

            _pageAnimation?.Pause();
            var start = Time.realtimeSinceStartup;
            const float duration = 0.22f;
            _pageAnimation = rootVisualElement.schedule.Execute(() =>
            {
                var t = (Time.realtimeSinceStartup - start) / duration;
                if (t >= 1f)
                {
                    from.style.left = new Length(0f, LengthUnit.Pixel);
                    to.style.left = new Length(0f, LengthUnit.Pixel);
                    from.style.display = DisplayStyle.None;
                    _pageAnimation?.Pause();
                    return;
                }

                var eased = t * t * (3f - 2f * t);
                var fromX = -direction * containerWidth * eased;
                var toX = direction * containerWidth * (1f - eased);
                from.style.left = new Length(fromX, LengthUnit.Pixel);
                to.style.left = new Length(toX, LengthUnit.Pixel);
            }).Every(16);
        }

        private VisualElement? GetPageByIndex(int index)
        {
            return index switch
            {
                0 => _pageWorld,
                1 => _pageRange,
                2 => _pageGenerate,
                _ => null
            };
        }
    }
}
