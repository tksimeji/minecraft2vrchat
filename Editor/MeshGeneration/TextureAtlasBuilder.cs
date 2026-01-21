using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace M2V.Editor.MeshGeneration
{
    internal sealed class TextureAtlasBuilder : ITextureAtlasBuilder
    {
        public Texture2D BuildTextureAtlasFromTextures(ZipArchive zip, HashSet<string> texturePaths, out Dictionary<string, Rect> uvByTexture, out Dictionary<string, TextureAlphaMode> alphaByTexture)
        {
            uvByTexture = new Dictionary<string, Rect>(StringComparer.Ordinal);
            alphaByTexture = new Dictionary<string, TextureAlphaMode>(StringComparer.Ordinal);
            if (zip == null)
            {
                return null;
            }

            var names = new List<string>(texturePaths);
            names.Sort(StringComparer.Ordinal);

            var textures = new List<Texture2D>(names.Count);
            var tileSize = 16;

            foreach (var fullName in names)
            {
                var tex = LoadTextureByPath(zip, fullName);
                if (tex == null)
                {
                    tex = LoadTextureByPath(zip, "minecraft:block/dirt");
                }

                if (tex == null)
                {
                    tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                    tex.SetPixels(BuildFallbackPixels(16, 16));
                    tex.Apply();
                }

                tileSize = Mathf.Max(tileSize, tex.width);
                textures.Add(tex);
            }

            var count = textures.Count;
            if (count == 0)
            {
                return null;
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
            atlas.SetPixels(BuildFallbackPixels(atlasSize, atlasSize));

            for (var i = 0; i < textures.Count; i++)
            {
                var col = i % columns;
                var row = i / columns;
                var x = col * tileSize;
                var y = row * tileSize;
                CopyTextureToAtlas(textures[i], atlas, x, y, tileSize);

                var rect = new Rect(
                    (float)x / atlasSize,
                    (float)y / atlasSize,
                    (float)tileSize / atlasSize,
                    (float)tileSize / atlasSize);

                var name = names[i];
                uvByTexture[name] = rect;
                alphaByTexture[name] = DetermineTextureAlphaMode(textures[i]);
            }

            atlas.Apply();
            return atlas;
        }

        private static Texture2D LoadTextureByPath(ZipArchive zip, string texturePath)
        {
            if (zip == null || string.IsNullOrEmpty(texturePath))
            {
                return null;
            }

            var path = texturePath;
            if (path.StartsWith("minecraft:"))
            {
                path = path.Substring("minecraft:".Length);
            }

            if (!path.StartsWith("block/") && !path.StartsWith("item/"))
            {
                path = "block/" + path;
            }

            var entry = zip.GetEntry($"assets/minecraft/textures/{path}.png");
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes))
            {
                return null;
            }

            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private static void CopyTextureToAtlas(Texture2D source, Texture2D atlas, int destX, int destY, int tileSize)
        {
            if (source == null || atlas == null)
            {
                return;
            }

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

        private static TextureAlphaMode DetermineTextureAlphaMode(Texture2D texture)
        {
            if (texture == null)
            {
                return TextureAlphaMode.Opaque;
            }

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
                if (pixel.a != 0)
                {
                    hasPartial = true;
                    break;
                }
            }

            if (!hasAlpha)
            {
                return TextureAlphaMode.Opaque;
            }

            return hasPartial ? TextureAlphaMode.Translucent : TextureAlphaMode.Cutout;
        }

        private static Color[] BuildFallbackPixels(int width, int height)
        {
            var colors = new Color[width * height];
            for (var i = 0; i < colors.Length; i++)
            {
                colors[i] = new Color(1f, 0f, 1f, 1f);
            }

            return colors;
        }
    }
}
