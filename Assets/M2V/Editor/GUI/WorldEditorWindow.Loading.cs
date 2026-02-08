#nullable enable

using UnityEngine;
using UnityEngine.UIElements;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private void ShowLoadingOverlay()
        {
            _loadingOverlay.style.display = DisplayStyle.Flex;
            SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingPreparing));
            StartLoadingAnimation();
            Repaint();
        }
        private void HideLoadingOverlay()
        {
            StopLoadingAnimation();
            _loadingOverlay.style.display = DisplayStyle.None;
            Repaint();
        }
        private void SetLoadingStatus(string text)
        {
            _loadingStatusLabel.text = text;
        }
        private void SetLoadingProgress(float progress)
        {
            StopLoadingAnimation();
            var clamped = Mathf.Clamp01(progress);
            _loadingBar.style.width = new Length(100f * clamped, LengthUnit.Percent);
        }
        private void StartLoadingAnimation()
        {
            _loadingAnimation?.Pause();
            var start = Time.realtimeSinceStartup;
            _loadingAnimation = rootVisualElement.schedule.Execute(() =>
            {
                var t = (Time.realtimeSinceStartup - start) * 3.0f;
                var pingPong = Mathf.PingPong(t, 1f);
                _loadingBar.style.width = new Length(20f + 80f * pingPong, LengthUnit.Percent);
            }).Every(16);
        }
        private void StopLoadingAnimation()
        {
            _loadingAnimation?.Pause();
            _loadingBar.style.width = new Length(0f, LengthUnit.Percent);
        }
    }
}