using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using M2V.Editor.Model;
using UnityEngine;

namespace M2V.Editor.Meshing
{
    internal sealed class BlockTintResolver
    {
        private static readonly Color32 DefaultGrass = new Color32(0x91, 0xBD, 0x59, 0xFF);
        private static readonly Color32 DefaultFoliage = new Color32(0x77, 0xAB, 0x2F, 0xFF);
        private static readonly Color32 DefaultWater = new Color32(0x3F, 0x76, 0xE4, 0xFF);
        private static readonly Color32 BirchLeaves = new Color32(0x80, 0xA7, 0x55, 0xFF);
        private static readonly Color32 SpruceLeaves = new Color32(0x61, 0x99, 0x61, 0xFF);
        private static readonly Color32 SwampGrass = new Color32(0x6A, 0x70, 0x39, 0xFF);
        private static readonly Color32 DarkForestTint = new Color32(0x28, 0x34, 0x0A, 0xFF);

        private readonly Texture2D _grassMap;
        private readonly Texture2D _foliageMap;
        private readonly Texture2D _waterMap;
        private readonly BiomeRegistry _biomeRegistry;

        internal BlockTintResolver(ZipArchive zip, BiomeRegistry biomeRegistry)
        {
            _biomeRegistry = biomeRegistry;
            _grassMap = LoadColormap(zip, "assets/minecraft/textures/colormap/grass.png");
            _foliageMap = LoadColormap(zip, "assets/minecraft/textures/colormap/foliage.png");
            _waterMap = LoadColormap(zip, "assets/minecraft/textures/colormap/water.png");
        }

        internal Color32[] BuildTintByBlock(int[] blocks, int sizeX, int sizeY, int sizeZ,
            IReadOnlyList<BlockStateKey> states, int[] biomes)
        {
            var total = blocks.Length;
            var tints = new Color32[total];
            var fallback = new Color32(255, 255, 255, 255);
            var biomeFallback = _biomeRegistry.GetBiomeInfo(_biomeRegistry.PlainsIndex);

            for (var i = 0; i < total; i++)
            {
                var blockId = blocks[i];
                if (blockId <= 0 || blockId >= states.Count)
                {
                    tints[i] = fallback;
                    continue;
                }

                var biome = biomeFallback;
                if (biomes != null && i < biomes.Length)
                {
                    biome = _biomeRegistry.GetBiomeInfo(biomes[i]);
                }

                tints[i] = ResolveTint(states[blockId].Name, biome);
            }

            return tints;
        }

        private Color32 ResolveTint(string blockName, BiomeInfo biome)
        {
            if (string.IsNullOrEmpty(blockName))
            {
                return new Color32(255, 255, 255, 255);
            }

            var name = blockName.StartsWith("minecraft:", StringComparison.Ordinal)
                ? blockName.Substring("minecraft:".Length)
                : blockName;

            if (name.IndexOf("water", StringComparison.Ordinal) >= 0)
            {
                if (biome.WaterColorOverride.HasValue)
                {
                    return biome.WaterColorOverride.Value;
                }

                return SampleBiomeColormap(_waterMap, biome.Temperature, biome.Downfall, DefaultWater);
            }

            if (name.IndexOf("spruce_leaves", StringComparison.Ordinal) >= 0)
            {
                return SpruceLeaves;
            }

            if (name.IndexOf("birch_leaves", StringComparison.Ordinal) >= 0)
            {
                return BirchLeaves;
            }

            if (name.IndexOf("leaves", StringComparison.Ordinal) >= 0 || name.IndexOf("leaf", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("vine", StringComparison.Ordinal) >= 0 || name.IndexOf("lily_pad", StringComparison.Ordinal) >= 0)
            {
                if (biome.FoliageColorOverride.HasValue)
                {
                    return biome.FoliageColorOverride.Value;
                }

                return SampleBiomeColormap(_foliageMap, biome.Temperature, biome.Downfall, DefaultFoliage);
            }

            if (name.IndexOf("grass", StringComparison.Ordinal) >= 0 || name.IndexOf("fern", StringComparison.Ordinal) >= 0)
            {
                if (biome.GrassColorOverride.HasValue)
                {
                    return ApplyGrassModifier(biome.GrassColorOverride.Value, biome.GrassColorModifier);
                }

                var color = SampleBiomeColormap(_grassMap, biome.Temperature, biome.Downfall, DefaultGrass);
                return ApplyGrassModifier(color, biome.GrassColorModifier);
            }

            return new Color32(255, 255, 255, 255);
        }

        private static Color32 ApplyGrassModifier(Color32 color, string? modifier)
        {
            if (string.IsNullOrEmpty(modifier))
            {
                return color;
            }

            if (string.Equals(modifier, "dark_forest", StringComparison.Ordinal))
            {
                var masked = new Color32((byte)(color.r & 0xFE), (byte)(color.g & 0xFE), (byte)(color.b & 0xFE), color.a);
                return new Color32(
                    (byte)((masked.r + DarkForestTint.r) / 2),
                    (byte)((masked.g + DarkForestTint.g) / 2),
                    (byte)((masked.b + DarkForestTint.b) / 2),
                    0xFF);
            }

            if (string.Equals(modifier, "swamp", StringComparison.Ordinal))
            {
                return SwampGrass;
            }

            return color;
        }

        private static Texture2D LoadColormap(ZipArchive zip, string entryPath)
        {
            if (zip == null)
            {
                return null;
            }

            var entry = zip.GetEntry(entryPath);
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

            return tex;
        }

        private static Color32 SampleBiomeColormap(Texture2D tex, float temperature, float downfall, Color32 fallback)
        {
            if (tex == null)
            {
                return fallback;
            }

            var adjTemp = Mathf.Clamp01(temperature);
            var adjDownfall = Mathf.Clamp01(downfall) * adjTemp;
            var u = 1f - adjTemp;
            var v = 1f - adjDownfall;
            var color = tex.GetPixelBilinear(u, v);
            return (Color32)color;
        }
    }
}
