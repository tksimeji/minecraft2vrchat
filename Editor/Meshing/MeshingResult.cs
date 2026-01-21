using UnityEngine;

namespace M2V.Editor.Meshing
{
    internal sealed class MeshingResult
    {
        internal Mesh Mesh { get; set; }
        internal Texture2D AtlasTexture { get; set; }
        internal string Message { get; set; }
        internal bool LogChunkOnce { get; set; }
    }
}