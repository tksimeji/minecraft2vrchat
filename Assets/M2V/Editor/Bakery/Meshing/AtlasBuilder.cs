#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using M2V.Editor.Minecraft;
using UnityEngine;

namespace M2V.Editor.Bakery.Meshing
{
    public sealed class AtlasBuilder
    {
        private const int DefaultTileSize = 16;

        private static readonly ResourceLocation FallbackTexture = ResourceLocation.Of("block/dirt");

        public static Texture2D BuildAtlas(
            AssetReader assetReader,
            HashSet<ResourceLocation> texturePaths,
            out Dictionary<ResourceLocation, RectF> uvByTexture,
            out Dictionary<ResourceLocation, TextureAlphaMode> alphaByTexture
        )
        {
            uvByTexture = new Dictionary<ResourceLocation, RectF>();
            alphaByTexture = new Dictionary<ResourceLocation, TextureAlphaMode>();

            var names = new List<ResourceLocation>(texturePaths);
            names.Sort((a, b) => string.CompareOrdinal(a.ToString(), b.ToString()));

            var textures = new List<Texture2D>(names.Count);
            var tileSize = DefaultTileSize;

            foreach (var texture in names.Select(fullName => TryLoadTexture(assetReader, fullName)))
            {
                var resolved = texture
                               ?? TryLoadTexture(assetReader, FallbackTexture)
                               ?? CreateFallbackTexture(DefaultTileSize);

                tileSize = Mathf.Max(tileSize, resolved.width);
                textures.Add(resolved);
            }

            var count = textures.Count;
            if (count == 0)
            {
                var fallback = new Texture2D(DefaultTileSize, DefaultTileSize, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
                fallback.SetPixels(CreateFallbackPixels(DefaultTileSize, DefaultTileSize));
                fallback.Apply();
                return FinalizeAtlas(fallback);
            }

            var columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            var rows = Mathf.CeilToInt((float)count / columns);
            var atlasSize = Mathf.NextPowerOfTwo(Mathf.Max(1, columns * tileSize));
            atlasSize = Mathf.Max(atlasSize, Mathf.NextPowerOfTwo(rows * tileSize));

            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            atlas.SetPixels(CreateFallbackPixels(atlasSize, atlasSize));

            for (var i = 0; i < textures.Count; i++)
            {
                var col = i % columns;
                var row = i / columns;
                var x = col * tileSize;
                var y = row * tileSize;
                DrawTile(textures[i], atlas, x, y, tileSize);

                var rect = new RectF(
                    (float)x / atlasSize,
                    (float)y / atlasSize,
                    (float)tileSize / atlasSize,
                    (float)tileSize / atlasSize
                );

                var name = names[i];
                uvByTexture[name] = rect;
                alphaByTexture[name] = GetAlphaMode(textures[i]);
            }

            atlas.Apply();
            return FinalizeAtlas(atlas);
        }

        private static Texture2D? TryLoadTexture(AssetReader assetReader, ResourceLocation texturePath)
        {
            if (texturePath.IsEmpty) return null;

            var fullPath = texturePath.ToAssetPath();
            if (!assetReader.TryReadBytes(fullPath, out var bytes))
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes)) return null;

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            return texture;
        }

        private static Texture2D CreateFallbackTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(CreateFallbackPixels(size, size));
            texture.Apply();
            return texture;
        }

        private static Color[] CreateFallbackPixels(int width, int height)
        {
            var colors = new Color[width * height];
            Array.Fill(colors, new Color(1f, 0f, 1f, 1f));
            return colors;
        }

        private static void DrawTile(Texture2D source, Texture2D atlas, int destX, int destY, int tileSize)
        {
            if (source == null || atlas == null) return;

            var width = source.width;
            var height = source.height;
            if (width == tileSize && height == tileSize)
            {
                var pixels = source.GetPixels();
                atlas.SetPixels(destX, destY, tileSize, tileSize, pixels);
                return;
            }

            var scaled = new Color[tileSize * tileSize];
            for (var y = 0; y < tileSize; y++)
            {
                var srcY = y * height / tileSize;
                for (var x = 0; x < tileSize; x++)
                {
                    var srcX = x * width / tileSize;
                    scaled[x + y * tileSize] = source.GetPixel(srcX, srcY);
                }
            }

            atlas.SetPixels(destX, destY, tileSize, tileSize, scaled);
        }

        private static TextureAlphaMode GetAlphaMode(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var hasAlpha = false;
            var hasPartial = false;
            foreach (var pixel in pixels)
            {
                if (pixel.a == 255)
                {
                    continue;
                }

                hasAlpha = true;
                if (pixel.a == 0)
                {
                    continue;
                }

                hasPartial = true;
                break;
            }

            if (!hasAlpha)
            {
                return TextureAlphaMode.Opaque;
            }

            return hasPartial ? TextureAlphaMode.Translucent : TextureAlphaMode.Cutout;
        }

        private static Texture2D FinalizeAtlas(Texture2D atlas)
        {
            atlas.filterMode = FilterMode.Point;
            atlas.wrapMode = TextureWrapMode.Repeat;
            return atlas;
        }
    }

    public enum TextureAlphaMode
    {
        Opaque,
        Cutout,
        Translucent
    }
}