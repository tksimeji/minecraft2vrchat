#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using fNbt;

namespace M2V.Editor.Minecraft.Model
{
    public sealed record SimpleModelSelectors
    {
        [JsonIgnore] public Dictionary<string, List<Variant>> Models { get; init; } = new(StringComparer.Ordinal);

        public List<Variant>? FindBestMatch(IReadOnlyDictionary<string, NbtTag> props)
        {
            List<Variant>? best = null;
            var bestScore = -1;

            foreach (var pair in Models)
            {
                if (!VariantSelector.Matches(pair.Key, props, out var score)
                    || score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                best = pair.Value;
            }

            return best;
        }
    }

    public sealed record MultiPartDefinition
    {
        [JsonIgnore] public List<Selector> Selectors { get; init; } = new();
    }

    public sealed record Selector
    {
        [JsonPropertyName("when")] public ICondition? Condition { get; init; }
        [JsonPropertyName("apply")] public List<Variant> Variant { get; init; } = new();

        public bool MatchesProperties(IReadOnlyDictionary<string, NbtTag> props) => Condition?.Test(props) ?? true;
    }

    internal sealed class SimpleModelSelectorsJsonConverter : JsonConverter<SimpleModelSelectors>
    {
        public override SimpleModelSelectors? Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return null;
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, List<Variant>>>(ref reader, options);
            return map != null ? new SimpleModelSelectors { Models = map } : new SimpleModelSelectors();
        }

        public override void Write(Utf8JsonWriter writer, SimpleModelSelectors value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Models, options);
        }
    }

    internal sealed class MultiPartDefinitionJsonConverter : JsonConverter<MultiPartDefinition>
    {
        public override MultiPartDefinition? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                return null;
            }

            var list = JsonSerializer.Deserialize<List<Selector>>(ref reader, options);
            return list != null ? new MultiPartDefinition { Selectors = list } : new MultiPartDefinition();
        }

        public override void Write(Utf8JsonWriter writer, MultiPartDefinition value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value.Selectors, options);
        }
    }
}