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

        private readonly Texture2D? _grassMap;
        private readonly Texture2D? _foliageMap;
        private readonly Texture2D? _waterMap;
        private readonly BiomeIndex _biomeRegistry;

        public BlockTintResolver(AssetReader assetReader, BiomeIndex biomeRegistry)
        {
            _biomeRegistry = biomeRegistry;
            _grassMap = LoadColormap(assetReader, ResourceLocation.Of("colormap/grass"));
            _foliageMap = LoadColormap(assetReader, ResourceLocation.Of("colormap/foliage"));
            _waterMap = LoadColormap(assetReader, ResourceLocation.Of("colormap/water"));
        }

        public Color32Byte[] BuildTintByBlock(Volume volume)
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

                var biome = _biomeRegistry.GetBiomeByIndex(volume.GetBiomeIdAtIndex(i));

                tints[i] = ResolveTint(volume.BlockStates[blockId].Name, biome);
            }

            return tints;
        }

        private Color32Byte ResolveTint(ResourceLocation blockName, Biome biome)
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
                return waterOverride ?? SampleBiomeColormap(_waterMap, biome.Temperature ?? 0f, biome.Downfall ?? 0f,
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

                return SampleBiomeColormap(_foliageMap, biome.Temperature ?? 0f, biome.Downfall ?? 0f, DefaultFoliage);
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

            var color = SampleBiomeColormap(_grassMap, biome.Temperature ?? 0f, biome.Downfall ?? 0f, DefaultGrass);
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

        private static Texture2D? LoadColormap(AssetReader assetReader, ResourceLocation texturePath)
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

            return texture;
        }

        private static Color32Byte SampleBiomeColormap(
            Texture2D? texture,
            float temperature,
            float downfall,
            Color32Byte fallback
        )
        {
            if (texture == null)
            {
                return fallback;
            }

            var adjTemp = Mathf.Clamp01(temperature);
            var adjDownfall = Mathf.Clamp01(downfall) * adjTemp;
            var u = 1f - adjTemp;
            var v = 1f - adjDownfall;
            var color = texture.GetPixelBilinear(u, v);
            var c32 = (Color32)color;
            return new Color32Byte(c32.r, c32.g, c32.b, c32.a);
        }
    }
}