#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2V.Editor.Minecraft.Model
{
    /// <seealso href="https://mcsrc.dev/#1/26.1-snapshot-5/net/minecraft/client/renderer/block/model/Variant"/>
    public sealed record Variant
    {
        [JsonPropertyName("model")] public ResourceLocation Model { get; init; } = ResourceLocation.Empty;
        [JsonPropertyName("x")] public int? X { get; init; }
        [JsonPropertyName("y")] public int? Y { get; init; }
        [JsonPropertyName("z")] public int? Z { get; init; }
        [JsonPropertyName("uvlock")] public bool? UvLock { get; init; }
        [JsonPropertyName("weight")] public int? Weight { get; init; }

        public int RotationX => X ?? 0;
        public int RotationY => Y ?? 0;
        public int RotationZ => Z ?? 0;
        public bool HasUvLock => UvLock ?? false;
    }

    internal sealed class VariantListJsonConverter : JsonConverter<List<Variant>>
    {
        public override List<Variant> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartArray:
                {
                    var list = new List<Variant>();
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                        {
                            break;
                        }

                        var item = JsonSerializer.Deserialize<Variant>(ref reader, options);
                        if (item != null)
                        {
                            list.Add(item);
                        }
                    }

                    return list;
                }
                case JsonTokenType.StartObject:
                {
                    var single = JsonSerializer.Deserialize<Variant>(ref reader, options);
                    return single != null ? new List<Variant> { single } : new List<Variant>();
                }
                case JsonTokenType.None:
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                case JsonTokenType.PropertyName:
                case JsonTokenType.Comment:
                case JsonTokenType.String:
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                default:
                    reader.Skip();
                    return new List<Variant>();
            }
        }

        public override void Write(Utf8JsonWriter writer, List<Variant> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}