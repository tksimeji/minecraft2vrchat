#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2V.Editor.Minecraft.Biome
{
    /// <summary>
    /// Represents a data structure <code>/worldgen/biome/*.json</code> in the game data.
    /// </summary>
    /// <seealso href="https://minecraft.wiki/w/Biome_definition"/>
    public sealed record Biome
    {
        public static readonly ResourceLocation Plains = ResourceLocation.Of("plains");

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [JsonPropertyName("temperature")] public float? Temperature { get; init; }
        [JsonPropertyName("downfall")] public float? Downfall { get; init; }
        [JsonPropertyName("effects")] public BiomeEffects? Effects { get; init; }
    }

    public sealed record BiomeEffects
    {
        [JsonPropertyName("grass_color")] public int? GrassColor { get; init; }
        [JsonPropertyName("foliage_color")] public int? FoliageColor { get; init; }
        [JsonPropertyName("water_color")] public int? WaterColor { get; init; }

        [JsonPropertyName("grass_color_modifier")]
        public string? GrassColorModifier { get; init; }
    }
}
