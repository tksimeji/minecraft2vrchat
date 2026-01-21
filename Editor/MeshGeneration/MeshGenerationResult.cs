using UnityEngine;

namespace M2V.Editor.MeshGeneration
{
    internal sealed class MeshGenerationResult
    {
        internal Mesh Mesh { get; set; }
        internal Texture2D AtlasTexture { get; set; }
        internal string Message { get; set; }
        internal bool LogChunkOnce { get; set; }
    }
}