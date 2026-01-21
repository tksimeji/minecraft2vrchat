using System.Collections.Generic;
using System.IO.Compression;
using M2V.Editor.Model;
using UnityEngine;

namespace M2V.Editor.MeshGeneration
{
    internal enum TextureAlphaMode
    {
        Opaque,
        Cutout,
        Translucent
    }

    internal interface IBlockStateSource
    {
        bool FillBlockStateIds(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max, int[] blocks,
            int sizeX, int sizeY, int sizeZ, List<BlockStateKey> states, ref bool logChunkOnce, bool logPaletteBounds);

        long CountBlocksInRange(string worldFolder, string dimensionId, Vector3Int min, Vector3Int max,
            ref bool logChunkOnce);
    }

    internal interface IModelRepository
    {
        List<List<ModelPlacement>> BuildBlockModels(List<BlockStateKey> states);
        List<bool> BuildFullCubeFlags(List<List<ModelPlacement>> modelCache);
        HashSet<string> CollectTexturePaths(List<List<ModelPlacement>> modelCache);
    }

    internal interface ITextureAtlasBuilder
    {
        Texture2D BuildTextureAtlasFromTextures(ZipArchive zip, HashSet<string> texturePaths,
            out Dictionary<string, Rect> uvByTexture, out Dictionary<string, TextureAlphaMode> alphaByTexture);
    }

    internal interface IMeshBuilder
    {
        Mesh BuildModelMesh(int[] blocks, int sizeX, int sizeY, int sizeZ, Vector3Int min,
            List<List<ModelPlacement>> modelCache, List<bool> fullCubeById, Dictionary<string, Rect> uvByTexture,
            Dictionary<string, TextureAlphaMode> alphaByTexture, bool applyCoordinateTransform);
    }
}