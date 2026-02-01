#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace M2V.Editor.Minecraft.Biome
{
    internal static class BiomeLoader
    {
        private const string BiomeRoot = "data/";
        private const string BiomeMarker = "/worldgen/biome/";

        internal static IEnumerable<(ResourceLocation Id, Biome Biome)> LoadAll(AssetReader reader)
        {
            foreach (var path in reader.EnumeratePaths("data/", ".json"))
            {
                if (!TryGetBiomeId(path, out var biomeId))
                {
                    continue;
                }

                if (TryLoadBiome(reader, path, biomeId, out var biome))
                {
                    yield return (biomeId, biome);
                }
            }
        }

        private static bool TryLoadBiome(
            AssetReader reader,
            string entryPath,
            ResourceLocation biomeKey,
            out Biome biome
        )
        {
            biome = null;
            if (reader == null || string.IsNullOrEmpty(entryPath) || biomeKey.IsEmpty)
            {
                return false;
            }

            return TryReadJson(reader, entryPath, out biome);
        }

        private static bool TryGetBiomeId(string entryPath, out ResourceLocation biomeId)
        {
            biomeId = ResourceLocation.Empty;
            if (string.IsNullOrEmpty(entryPath) || !entryPath.StartsWith(BiomeRoot, StringComparison.Ordinal))
            {
                return false;
            }

            var markerIndex = entryPath.IndexOf(BiomeMarker, StringComparison.Ordinal);
            if (markerIndex <= BiomeRoot.Length)
            {
                return false;
            }

            var namespacePart = entryPath.Substring(BiomeRoot.Length, markerIndex - BiomeRoot.Length);
            var relative = entryPath.Substring(markerIndex + BiomeMarker.Length);
            if (!relative.EndsWith(".json", StringComparison.Ordinal))
            {
                return false;
            }

            var path = relative.Substring(0, relative.Length - ".json".Length);
            if (string.IsNullOrEmpty(namespacePart) || string.IsNullOrEmpty(path))
            {
                return false;
            }

            biomeId = ResourceLocation.Parse($"{namespacePart}:{path}");
            return true;
        }

        private static bool TryReadJson<T>(AssetReader reader, string path, out T result)
            where T : class
        {
            result = null;
            if (reader == null || string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!reader.TryReadText(path, out var json) || string.IsNullOrEmpty(json))
            {
                return false;
            }

            try
            {
                result = JsonSerializer.Deserialize<T>(json, Biome.JsonOptions);
                return result != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}