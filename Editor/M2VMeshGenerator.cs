using UnityEngine;
using M2V.Editor.Meshing;

namespace M2V.Editor
{
    public static class M2VMeshGenerator
    {
        public struct Options
        {
            public bool UseGreedy;
            public bool ApplyCoordinateTransform;
            public bool LogSliceStats;
            public bool LogPaletteBounds;
            public bool UseTextureAtlas;
        }

        public static Mesh GenerateMesh(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, string minecraftJarPath, System.IO.FileSystemInfo resourcePack, System.IO.FileSystemInfo dataPack, Options options, ref bool logChunkOnce, out string message, out Texture2D atlasTexture)
        {
            var useCase = CreateUseCase();
            var request = new MeshingRequest
            {
                WorldFolder = worldFolder,
                DimensionId = dimensionId,
                Min = min,
                Max = max,
                MinecraftJarPath = minecraftJarPath,
                ResourcePack = resourcePack,
                DataPack = dataPack,
                Options = options,
                LogChunkOnce = logChunkOnce
            };

            var result = useCase.Execute(request);
            logChunkOnce = result.LogChunkOnce;
            message = result.Message;
            atlasTexture = result.AtlasTexture;
            return result.Mesh;
        }

        public static long CountBlocksInRange(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, ref bool logChunkOnce)
        {
            var blockSource = new WorldBlockSource();
            return blockSource.CountBlocksInRange(worldFolder, dimensionId, min, max, ref logChunkOnce);
        }

        private static MeshingUseCase CreateUseCase()
        {
            return new MeshingUseCase(
                new WorldBlockSource(),
                new ModelMeshBuilder(),
                new TextureAtlasBuilder(),
                assets => new ModelRepository(assets));
        }
    }
}
