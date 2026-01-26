using System.Collections.Generic;
using M2V.Editor.Model;
using M2V.Editor.World.Block;
using UnityEngine;

namespace M2V.Editor.Meshing
{
    internal enum TextureAlphaMode
    {
        Opaque,
        Cutout,
        Translucent
    }

    internal interface IModelRepository
    {
        List<List<ModelPlacement>> BuildBlockModels(List<BlockState> states);
        List<bool> BuildFullCubeFlags(List<List<ModelPlacement>> modelCache);
        HashSet<string> CollectTexturePaths(List<List<ModelPlacement>> modelCache);
    }

    internal interface ITextureAtlasBuilder
    {
        Texture2D BuildTextureAtlasFromTextures(IAssetReader assetReader, HashSet<string> texturePaths, out Dictionary<string, Rect> uvByTexture, out Dictionary<string, TextureAlphaMode> alphaByTexture);
    }

    internal interface IMeshBuilder
    {
        Mesh BuildModelMesh(int[] blocks, int sizeX, int sizeY, int sizeZ, Vector3Int min, List<List<ModelPlacement>> modelCache, List<bool> fullCubeById, IReadOnlyList<Color32> tintByBlock, Dictionary<string, Rect> uvByTexture, Dictionary<string, TextureAlphaMode> alphaByTexture, bool applyCoordinateTransform);
    }
}
