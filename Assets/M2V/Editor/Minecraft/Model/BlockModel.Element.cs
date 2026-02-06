#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace M2V.Editor.Minecraft.Model
{
    public sealed record BlockElement
    {
        public static Vector3[] GetFaceQuadCorners(Direction direction, Vector3[] corners)
        {
            return direction switch
            {
                Direction.North => new[] { corners[0], corners[1], corners[2], corners[3] },
                Direction.South => new[] { corners[5], corners[4], corners[7], corners[6] },
                Direction.West => new[] { corners[0], corners[4], corners[7], corners[3] },
                Direction.East => new[] { corners[5], corners[1], corners[2], corners[6] },
                Direction.Down => new[] { corners[0], corners[1], corners[5], corners[4] },
                _ => new[] { corners[3], corners[2], corners[6], corners[7] }
            };
        }

        [JsonPropertyName("from")] public Vector3 From { get; init; }
        [JsonPropertyName("to")] public Vector3 To { get; init; }
        [JsonPropertyName("rotation")] public BlockElementRotation? Rotation { get; init; }
        [JsonPropertyName("shade")] public bool? Shade { get; init; }
        [JsonPropertyName("light_emission")] public int? LightEmission { get; init; }
        [JsonPropertyName("faces")] public Dictionary<Direction, BlockElementFace>? Faces { get; init; }

        public bool IsFullCube => From == Vector3.zero && To == new Vector3(16f, 16f, 16f);

        public Vector3[] GetCornerPositions()
        {
            var min = From / 16f;
            var max = To / 16f;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(min.x, max.y, max.z)
            };
        }

        public Vector4 GetDefaultUv(Direction direction)
        {
            var min = From;
            var max = To;
            return direction switch
            {
                Direction.North or Direction.South => new Vector4(min.x, 16f - max.y, max.x, 16f - min.y),
                Direction.West or Direction.East => new Vector4(min.z, 16f - max.y, max.z, 16f - min.y),
                _ => new Vector4(min.x, min.z, max.x, max.z)
            };
        }
    }

    public sealed record BlockElementRotation
    {
        [JsonPropertyName("origin")] public Vector3 Origin { get; init; }
        [JsonPropertyName("axis")] public Axis? Axis { get; init; }
        [JsonPropertyName("angle")] public float Angle { get; init; }
        [JsonPropertyName("rescale")] public bool? Rescale { get; init; }

        public Vector3 ToToOriginVector() => Origin / 16f;

        public Vector3 ToAxisVector()
        {
            return Axis switch
            {
                global::M2V.Editor.Minecraft.Axis.X => Vector3.right,
                global::M2V.Editor.Minecraft.Axis.Y => Vector3.up,
                global::M2V.Editor.Minecraft.Axis.Z => Vector3.forward,
                _ => Vector3.up
            };
        }
    }

    public sealed record BlockElementFace
    {
        [JsonPropertyName("uv")] public float[]? Uv { get; init; }
        [JsonPropertyName("texture")] public string? Texture { get; init; }
        [JsonPropertyName("cullface")]
        [JsonConverter(typeof(DirectionJsonConverter))]
        public Direction? CullFace { get; init; }
        [JsonPropertyName("rotation")] public int Rotation { get; init; }
        [JsonPropertyName("tintindex")] public int? TintIndex { get; init; }

        public Vector4 GetUv(BlockElement element, Direction direction)
        {
            return Uv is { Length: >= 4 }
                ? new Vector4(Uv[0], Uv[1], Uv[2], Uv[3])
                : element.GetDefaultUv(direction);
        }
    }

    internal sealed class BlockElementFacesJsonConverter : JsonConverter<Dictionary<Direction, BlockElementFace>>
    {
        public override Dictionary<Direction, BlockElementFace> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return new Dictionary<Direction, BlockElementFace>();
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var result = new Dictionary<Direction, BlockElementFace>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!DirectionJsonConverter.TryParse(property.Name, out var direction))
                {
                    continue;
                }

                var face = JsonSerializer.Deserialize<BlockElementFace>(property.Value.GetRawText(), options);
                if (face != null)
                {
                    result[direction] = face;
                }
            }

            return result;
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<Direction, BlockElementFace> value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var pair in value)
            {
                writer.WritePropertyName(pair.Key.ToString().ToLowerInvariant());
                JsonSerializer.Serialize(writer, pair.Value, options);
            }

            writer.WriteEndObject();
        }
    }
}
