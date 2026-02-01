using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Bakery.Meshing;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private void ShowLoadingOverlay()
        {
            if (_loadingOverlay == null)
            {
                return;
            }

            _loadingOverlay.style.display = DisplayStyle.Flex;
            SetLoadingStatus(Localization.Get(_state.Language, Localization.Keys.LoadingPreparing));
            StartLoadingAnimation();
            Repaint();
        }
        private void HideLoadingOverlay()
        {
            if (_loadingOverlay == null)
            {
                return;
            }

            StopLoadingAnimation();
            _loadingOverlay.style.display = DisplayStyle.None;
            Repaint();
        }
        private void SetLoadingStatus(string text)
        {
            if (_loadingStatusLabel == null)
            {
                return;
            }

            _loadingStatusLabel.text = text;
        }
        private void StartLoadingAnimation()
        {
            if (_loadingBar == null)
            {
                return;
            }

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
            if (_loadingBar != null)
            {
                _loadingBar.style.width = new Length(0f, LengthUnit.Percent);
            }
        }
    }
}
