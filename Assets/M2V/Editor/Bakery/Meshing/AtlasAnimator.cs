#nullable enable
using UnityEditor;
using UnityEngine;

namespace M2V.Editor.Bakery.Meshing
{
    [ExecuteAlways]
    public sealed class AtlasAnimator : MonoBehaviour
    {
        private Texture2D? _atlas;
        private AtlasAnimation? _animation;
        private double _startTime;

        public void Initialize(Texture2D atlas, AtlasAnimation animation)
        {
            _atlas = atlas;
            _animation = animation;
            _startTime = GetTimeSeconds();
        }

        private void Update()
        {
            if (_atlas == null || _animation == null)
            {
                return;
            }

            var time = GetTimeSeconds() - _startTime;
            _animation.Update(_atlas, time);
        }

        private static double GetTimeSeconds()
        {
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
            {
                return EditorApplication.timeSinceStartup;
            }
#endif
            return Time.unscaledTimeAsDouble;
        }
    }
}