#nullable enable

using System;
using M2V.Editor.Bakery.Meshing;
using M2V.Editor.Minecraft;
using M2V.Editor.Minecraft.Biome;
using UnityEngine;

namespace M2V.Editor.Bakery.Tinting
{
    public sealed class BlockTintResolver
    {
        private static readonly Color32Byte DefaultGrass = new(0x91, 0xBD, 0x59, 0xFF);
        private static readonly Color32Byte DefaultFoliage = new(0x77, 0xAB, 0x2F, 0xFF);
        private static readonly Color32Byte DefaultWater = new(0x3F, 0x76, 0xE4, 0xFF);
        private static readonly Color32Byte BirchLeaves = new(0x80, 0xA7, 0x55, 0xFF);
        private static readonly Color32Byte SpruceLeaves = new(0x61, 0x99, 0x61, 0xFF);
        private static readonly Color32Byte SwampGrass = new(0x6A, 0x70, 0x39, 0xFF);
        private static readonly Color32Byte DarkForestTint = new(0x28, 0x34, 0x0A, 0xFF);

        public sealed record Colormap(int Width, int Height, Color32[] Pixels);
        public sealed record ColormapSet(Colormap? Grass, Colormap? Foliage, Colormap? Water);

        private readonly ColormapSet _colormaps;
        private readonly BiomeIndex _biomeRegistry;

        public BlockTintResolver(ColormapSet colormaps, BiomeIndex biomeRegistry)
        {
            _colormaps = colormaps;
            _biomeRegistry = biomeRegistry;
        }

        public static ColormapSet LoadColormaps(AssetReader assetReader)
        {
            var grass = LoadColormap(assetReader, ResourceLocation.Of("colormap/grass"));
            var foliage = LoadColormap(assetReader, ResourceLocation.Of("colormap/foliage"));
            var water = LoadColormap(assetReader, ResourceLocation.Of("colormap/water"));
            return new ColormapSet(grass, foliage, water);
        }

        public Color32Byte[] BuildTintByBlock(Volume volume)
        {
            return BuildTintByBlock(volume, _biomeRegistry, _colormaps);
        }

        public static Color32Byte[] BuildTintByBlock(Volume volume, BiomeIndex biomeRegistry, ColormapSet colormaps)
        {
            var total = volume.TotalBlocks;
            var tints = new Color32Byte[total];
            var fallback = new Color32Byte(255, 255, 255, 255);

            for (var i = 0; i < total; i++)
            {
                var blockId = volume.GetBlockIdAtIndex(i);
                if (blockId <= 0 || blockId >= volume.BlockStateCount)
                {
                    tints[i] = fallback;
                    continue;
                }

                var biome = biomeRegistry.GetBiomeByIndex(volume.GetBiomeIdAtIndex(i));

                tints[i] = ResolveTint(volume.BlockStates[blockId].Name, biome, colormaps);
            }

            return tints;
        }

        private static Color32Byte ResolveTint(ResourceLocation blockName, Biome biome, ColormapSet colormaps)
        {
            if (blockName.IsEmpty)
            {
                return new Color32Byte(255, 255, 255, 255);
            }

            var name = string.Equals(blockName.Namespace, ResourceLocation.MinecraftNamespace, StringComparison.Ordinal)
                ? blockName.Path
                : blockName.ToString();

            if (name.IndexOf("water", StringComparison.Ordinal) >= 0)
            {
                var waterOverride = ToColor(biome.Effects?.WaterColor);
                return waterOverride ?? SampleBiomeColormap(colormaps.Water, biome.Temperature ?? 0f, biome.Downfall ?? 0f,
                    DefaultWater);
            }

            if (name.IndexOf("spruce_leaves", StringComparison.Ordinal) >= 0)
            {
                return SpruceLeaves;
            }

            if (name.IndexOf("birch_leaves", StringComparison.Ordinal) >= 0)
            {
                return BirchLeaves;
            }

            if (name.IndexOf("leaves", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("leaf", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("vine", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("lily_pad", StringComparison.Ordinal) >= 0)
            {
                var foliageOverride = ToColor(biome.Effects?.FoliageColor);
                if (foliageOverride.HasValue)
                {
                    return foliageOverride.Value;
                }

                return SampleBiomeColormap(colormaps.Foliage, biome.Temperature ?? 0f, biome.Downfall ?? 0f, DefaultFoliage);
            }

            if (name.IndexOf("grass", StringComparison.Ordinal) < 0
                && name.IndexOf("fern", StringComparison.Ordinal) < 0)
            {
                return new Color32Byte(255, 255, 255, 255);
            }
                
            var grassOverride = ToColor(biome.Effects?.GrassColor);
            if (grassOverride.HasValue)
            {
                return ApplyGrassModifier(grassOverride.Value, biome.Effects?.GrassColorModifier);
            }

            var color = SampleBiomeColormap(colormaps.Grass, biome.Temperature ?? 0f, biome.Downfall ?? 0f, DefaultGrass);
            return ApplyGrassModifier(color, biome.Effects?.GrassColorModifier);
        }

        private static Color32Byte ApplyGrassModifier(Color32Byte color, string? modifier)
        {
            if (string.IsNullOrEmpty(modifier))
            {
                return color;
            }

            if (string.Equals(modifier, "dark_forest", StringComparison.Ordinal))
            {
                var masked = new Color32Byte((byte)(color.R & 0xFE), (byte)(color.G & 0xFE), (byte)(color.B & 0xFE),
                    color.A);
                return new Color32Byte(
                    (byte)((masked.R + DarkForestTint.R) / 2),
                    (byte)((masked.G + DarkForestTint.G) / 2),
                    (byte)((masked.B + DarkForestTint.B) / 2),
                    0xFF);
            }

            return string.Equals(modifier, "swamp", StringComparison.Ordinal) ? SwampGrass : color;
        }

        private static Color32Byte? ToColor(int? rgb)
        {
            return rgb.HasValue ? Color32Byte.FromInt(rgb.Value) : null;
        }

        private static Colormap? LoadColormap(AssetReader assetReader, ResourceLocation texturePath)
        {
            var entryPath = texturePath.ToAssetPath();
            if (string.IsNullOrEmpty(entryPath) || !assetReader.TryReadBytes(entryPath, out var bytes))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                return null;
            }

            var pixels = texture.GetPixels32();
            var colormap = new Colormap(texture.width, texture.height, pixels);
            UnityEngine.Object.DestroyImmediate(texture);
            return colormap;
        }

        private static Color32Byte SampleBiomeColormap(
            Colormap? colormap,
            float temperature,
            float downfall,
            Color32Byte fallback
        )
        {
            if (colormap == null)
            {
                return fallback;
            }

            var adjTemp = Mathf.Clamp01(temperature);
            var adjDownfall = Mathf.Clamp01(downfall) * adjTemp;
            var u = 1f - adjTemp;
            var v = 1f - adjDownfall;
            var c32 = SampleBilinear(colormap, u, v);
            return new Color32Byte(c32.r, c32.g, c32.b, c32.a);
        }

        private static Color32 SampleBilinear(Colormap colormap, float u, float v)
        {
            var width = colormap.Width;
            var height = colormap.Height;
            if (width <= 0 || height <= 0 || colormap.Pixels.Length == 0)
            {
                return new Color32(255, 255, 255, 255);
            }

            var x = Mathf.Clamp(u * (width - 1), 0f, width - 1);
            var y = Mathf.Clamp(v * (height - 1), 0f, height - 1);

            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var y1 = Mathf.Min(y0 + 1, height - 1);

            var tx = x - x0;
            var ty = y - y0;

            var c00 = colormap.Pixels[x0 + y0 * width];
            var c10 = colormap.Pixels[x1 + y0 * width];
            var c01 = colormap.Pixels[x0 + y1 * width];
            var c11 = colormap.Pixels[x1 + y1 * width];

            var r = Mathf.Lerp(
                Mathf.Lerp(c00.r, c10.r, tx),
                Mathf.Lerp(c01.r, c11.r, tx),
                ty
            );
            var g = Mathf.Lerp(
                Mathf.Lerp(c00.g, c10.g, tx),
                Mathf.Lerp(c01.g, c11.g, tx),
                ty
            );
            var b = Mathf.Lerp(
                Mathf.Lerp(c00.b, c10.b, tx),
                Mathf.Lerp(c01.b, c11.b, tx),
                ty
            );
            var a = Mathf.Lerp(
                Mathf.Lerp(c00.a, c10.a, tx),
                Mathf.Lerp(c01.a, c11.a, tx),
                ty
            );

            return new Color32((byte)r, (byte)g, (byte)b, (byte)a);
        }
    }
}