using UnityEngine;

namespace M2V.Editor.Meshing
{
    internal sealed class MeshingRequest
    {
        internal string WorldFolder { get; set; }
        internal string DimensionId { get; set; }
        internal Vector3Int Min { get; set; }
        internal Vector3Int Max { get; set; }
        internal string MinecraftJarPath { get; set; }
        internal System.IO.FileSystemInfo ResourcePack { get; set; }
        internal System.IO.FileSystemInfo DataPack { get; set; }
        internal M2VMeshGenerator.Options Options { get; set; }
        internal bool LogChunkOnce { get; set; }
    }
}
