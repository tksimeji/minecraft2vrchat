using UnityEditor;

namespace M2V.Editor
{
    public class M2VEditorWindow : EditorWindow
    { 
        [MenuItem("Tools/Minecraft2VRChat")]
        public static void Open()
        {
            var window = GetWindow<M2VEditorWindow>("Minecraft2VRChat");
            window.Show();
        }
        
        private void OnGUI()
        {
        }
    }
}