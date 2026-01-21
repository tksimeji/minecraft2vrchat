#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace M2V.Editor.Meshing
{
    internal sealed class BiomeRegistry
    {
        private const string BiomeRoot = "data/";
        private const string BiomeMarker = "/worldgen/biome/";
        private const string PlainsId = "minecraft:plains";

        private readonly Dictionary<string, int> _indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<BiomeInfo> _biomes = new List<BiomeInfo>();
        private readonly int _plainsIndex;

        internal BiomeRegistry(ZipArchive zip)
        {
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.EndsWith(".json", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetBiomeId(entry.FullName, out var biomeId))
                {
                    continue;
                }

                var info = TryReadBiomeInfo(entry, biomeId);
                if (info != null)
                {
                    AddOrUpdate(info);
                }
            }

            if (_indexByName.TryGetValue(PlainsId, out _plainsIndex)) return;
            var fallback = new BiomeInfo(PlainsId, 0.8f, 0.4f, null, null, null, null);
            _plainsIndex = AddOrUpdate(fallback);
        }

        internal int PlainsIndex => _plainsIndex;

        internal int GetBiomeIndex(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return _plainsIndex;
            }

            return _indexByName.TryGetValue(name, out var index) ? index : _plainsIndex;
        }

        internal BiomeInfo GetBiomeInfo(int index)
        {
            if (index >= 0 && index < _biomes.Count)
            {
                return _biomes[index];
            }

            return _biomes[_plainsIndex];
        }

        private int AddOrUpdate(BiomeInfo info)
        {
            if (_indexByName.TryGetValue(info.Name, out var index))
            {
                _biomes[index] = info;
                return index;
            }

            index = _biomes.Count;
            _biomes.Add(info);
            _indexByName[info.Name] = index;
            return index;
        }

        private static bool TryGetBiomeId(string entryPath, out string biomeId)
        {
            biomeId = string.Empty;
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

            biomeId = $"{namespacePart}:{path}";
            return true;
        }

        private static BiomeInfo? TryReadBiomeInfo(ZipArchiveEntry entry, string biomeId)
        {
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                var obj = JObject.Parse(json);
                var temp = ReadFloat(obj["temperature"], 0.0f);
                var downfall = ReadFloat(obj["downfall"], 0.0f);

                Color32? grass = null;
                Color32? foliage = null;
                Color32? water = null;
                string? grassModifier = null;

                if (obj["effects"] is JObject effects)
                {
                    grass = ReadColor(effects["grass_color"]);
                    foliage = ReadColor(effects["foliage_color"]);
                    water = ReadColor(effects["water_color"]);
                    grassModifier = effects["grass_color_modifier"]?.Value<string>();
                }

                return new BiomeInfo(biomeId, temp, downfall, grass, foliage, water, grassModifier);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static float ReadFloat(JToken? token, float fallback)
        {
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                return token.Value<float>();
            }

            if (token.Type == JTokenType.String && float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return fallback;
        }

        private static Color32? ReadColor(JToken? token)
        {
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return ColorFromRgbInt(token.Value<int>());
            }

            if (token.Type == JTokenType.String && int.TryParse(token.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return ColorFromRgbInt(value);
            }

            return null;
        }

        private static Color32 ColorFromRgbInt(int value)
        {
            var r = (byte)((value >> 16) & 0xFF);
            var g = (byte)((value >> 8) & 0xFF);
            var b = (byte)(value & 0xFF);
            return new Color32(r, g, b, 0xFF);
        }
    }

    internal sealed class BiomeInfo
    {
        internal BiomeInfo(string name, float temperature, float downfall, Color32? grassOverride, Color32? foliageOverride, Color32? waterOverride, string? grassModifier)
        {
            Name = name;
            Temperature = temperature;
            Downfall = downfall;
            GrassColorOverride = grassOverride;
            FoliageColorOverride = foliageOverride;
            WaterColorOverride = waterOverride;
            GrassColorModifier = grassModifier;
        }

        internal string Name { get; }
        internal float Temperature { get; }
        internal float Downfall { get; }
        internal Color32? GrassColorOverride { get; }
        internal Color32? FoliageColorOverride { get; }
        internal Color32? WaterColorOverride { get; }
        internal string? GrassColorModifier { get; }
    }
}
