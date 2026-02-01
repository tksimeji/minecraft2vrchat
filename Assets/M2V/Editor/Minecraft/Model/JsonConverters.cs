#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEngine;

namespace M2V.Editor.Minecraft.Model
{
    internal sealed class Vector3JsonConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                reader.Skip();
                return Vector3.zero;
            }

            var values = new float[3];
            var index = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.Number && index < 3)
                {
                    values[index++] = reader.GetSingle();
                }
                else
                {
                    reader.Skip();
                }
            }

            return new Vector3(values[0], values[1], values[2]);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.x);
            writer.WriteNumberValue(value.y);
            writer.WriteNumberValue(value.z);
            writer.WriteEndArray();
        }
    }
}