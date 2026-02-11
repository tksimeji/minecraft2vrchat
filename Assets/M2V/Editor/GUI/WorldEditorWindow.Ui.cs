#nullable enable

using System;
using UnityEditor;
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
            _jarWarningCallout = rootVisualElement.Q<VisualElement>("jarWarningCallout");
            _jarWarningText = rootVisualElement.Q<Label>("jarWarningText");
            _jarHoverBalloon = rootVisualElement.Q<VisualElement>("jarHoverBalloon");
            _jarHoverBalloonText = rootVisualElement.Q<Label>("jarHoverBalloonText");
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
            HideJarWarningCallout();
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
            RegisterJarWarningHoverPulse();
        }

        private void BindNavigation()
        {
            _currentPageIndex = 0;
            _nextWorldButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log("[M2V] Navigate: Worlds -> Range");
                if (_state.GetSelectedWorld() == null)
                {
                    ShowBalloonAt(
                        Localization.Get(_state.Language, Localization.Keys.SelectWorldBalloon),
                        evt.position
                    );
                    return;
                }
                if (!TryEnterRange())
                {
                    return;
                }

                SetPage(1);
            });

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
            _stepRange.RegisterCallback<ClickEvent>(evt =>
            {
                if (_state.GetSelectedWorld() == null)
                {
                    ShowBalloonAt(
                        Localization.Get(_state.Language, Localization.Keys.SelectWorldBalloon),
                        evt.position
                    );
                    return;
                }
                if (TryEnterRange())
                {
                    SetPage(1);
                }
            });
            _stepGenerate.RegisterCallback<ClickEvent>(evt =>
            {
                if (_state.GetSelectedWorld() == null)
                {
                    ShowBalloonAt(
                        Localization.Get(_state.Language, Localization.Keys.SelectWorldBalloon),
                        evt.position
                    );
                    return;
                }
                if (TryEnterRange())
                {
                    SetPage(2);
                }
            });
        }

        private bool IsUiReady(Button? clearButton, Button? customImportButton, Button? reloadButton)
        {
            return clearButton != null && customImportButton != null && reloadButton != null;
        }

        private bool TryEnterRange()
        {
            var world = _state.GetSelectedWorld();
            if (world == null)
            {
                return false;
            }

            if (HasMinecraftJarForWorld(world))
            {
                HideJarWarningCallout();
                return true;
            }

            ShowJarWarningCallout(world.VersionName);
            return false;
        }

        private bool HasMinecraftJarForWorld(M2V.Editor.Minecraft.World.World world)
        {
            var versionName = world.VersionName;
            if (string.IsNullOrEmpty(versionName))
            {
                return false;
            }

            var jarPath = GetMinecraftVersionJarPath(versionName);
            return !string.IsNullOrEmpty(jarPath);
        }

        private void ShowJarWarningCallout(string? versionName)
        {
            if (_jarWarningCallout == null || _jarWarningText == null)
            {
                return;
            }

            var displayVersion = string.IsNullOrEmpty(versionName) ? "x.x.x" : versionName;
            var template = Localization.Get(_state.Language, Localization.Keys.JarMissingCallout);
            _jarWarningText.text = string.Format(template, displayVersion);
            _jarWarningCallout.style.display = DisplayStyle.Flex;
            _jarMissingActive = true;
            _lastHoverTarget = null;
            _jarWarningCallout.style.scale = new Scale(Vector3.one);
            HideJarHoverBalloon();
        }

        private void HideJarWarningCallout()
        {
            if (_jarWarningCallout == null)
            {
                return;
            }

            _jarWarningCallout.style.display = DisplayStyle.None;
            _jarMissingActive = false;
            _lastHoverTarget = null;
            _calloutPulse?.Pause();
            HideJarHoverBalloon();
        }

        private void RegisterJarWarningHoverPulse()
        {
            if (_rootElement == null)
            {
                return;
            }

            _rootElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                var worldPos = _rootElement.LocalToWorld(evt.position);
                var hoverTarget = GetJarWarningHoverTarget(worldPos);
                if (_state.GetSelectedWorld() == null)
                {
                    if (hoverTarget == null)
                    {
                        _lastHoverTarget = null;
                        HideJarHoverBalloon();
                        return;
                    }

                    ShowBalloonAt(
                        Localization.Get(_state.Language, Localization.Keys.SelectWorldBalloon),
                        worldPos,
                        autoHideSeconds: 0f
                    );
                    _lastHoverTarget = hoverTarget;
                    return;
                }

                if (!_jarMissingActive || _jarWarningCallout == null)
                {
                    HideJarHoverBalloon();
                    return;
                }

                if (hoverTarget == null)
                {
                    _lastHoverTarget = null;
                    HideJarHoverBalloon();
                    return;
                }

                ShowJarHoverBalloon(worldPos);
                if (ReferenceEquals(hoverTarget, _lastHoverTarget))
                {
                    return;
                }

                _lastHoverTarget = hoverTarget;
                PulseCallout();
            });

            _rootElement.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _lastHoverTarget = null;
                HideJarHoverBalloon();
            });
        }

        private VisualElement? GetJarWarningHoverTarget(Vector2 position)
        {
            if (_nextWorldButton != null && _nextWorldButton.worldBound.Contains(position))
            {
                return _nextWorldButton;
            }
            if (_stepRange != null && _stepRange.worldBound.Contains(position))
            {
                return _stepRange;
            }
            if (_stepGenerate != null && _stepGenerate.worldBound.Contains(position))
            {
                return _stepGenerate;
            }
            if (_nextRangeButton != null && _nextRangeButton.worldBound.Contains(position))
            {
                return _nextRangeButton;
            }

            return null;
        }

        private void PulseCallout()
        {
            if (_jarWarningCallout == null)
            {
                return;
            }

            _calloutPulse?.Pause();
            var start = Time.realtimeSinceStartup;
            const float duration = 0.28f;
            _calloutPulse = _jarWarningCallout.schedule.Execute(() =>
            {
                var t = (Time.realtimeSinceStartup - start) / duration;
                if (t >= 1f)
                {
                    _jarWarningCallout.style.scale = new Scale(Vector3.one);
                    _calloutPulse?.Pause();
                    return;
                }

                var eased = t < 0.5f ? t * 2f : (1f - t) * 2f;
                var scale = Mathf.Lerp(1f, 1.08f, eased);
                _jarWarningCallout.style.scale = new Scale(new Vector3(scale, scale, 1f));
            }).Every(16);
        }

        private void ShowJarHoverBalloon(Vector2 worldPosition)
        {
            if (_jarHoverBalloon == null || _jarHoverBalloonText == null)
            {
                return;
            }

            var world = _state.GetSelectedWorld();
            var displayVersion = string.IsNullOrEmpty(world?.VersionName) ? "x.x.x" : world!.VersionName;
            var template = Localization.Get(_state.Language, Localization.Keys.JarMissingCallout);
            ShowBalloonAt(string.Format(template, displayVersion), worldPosition, autoHideSeconds: 0f);
        }

        private void ShowBalloonAt(string message, Vector2 worldPosition, float autoHideSeconds = 2f)
        {
            if (_jarHoverBalloon == null || _jarHoverBalloonText == null)
            {
                return;
            }

            _jarHoverBalloonText.text = message;

            var parent = _jarHoverBalloon.parent;
            if (parent == null)
            {
                return;
            }

            var local = parent.WorldToLocal(worldPosition);
            _jarHoverBalloon.style.left = local.x + 12f;
            _jarHoverBalloon.style.top = local.y + 12f;
            _jarHoverBalloon.style.display = DisplayStyle.Flex;

            _balloonAutoHide?.Pause();
            if (autoHideSeconds > 0f)
            {
                _balloonAutoHide = _jarHoverBalloon.schedule.Execute(HideJarHoverBalloon)
                    .StartingIn((long)(autoHideSeconds * 1000f));
            }
        }

        private void HideJarHoverBalloon()
        {
            if (_jarHoverBalloon == null)
            {
                return;
            }

            _balloonAutoHide?.Pause();
            _jarHoverBalloon.style.display = DisplayStyle.None;
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
