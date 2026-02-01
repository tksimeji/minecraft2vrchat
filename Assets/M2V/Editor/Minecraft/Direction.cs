#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2V.Editor.Minecraft
{
    public enum Direction
    {
        North,
        South,
        West,
        East,
        Up,
        Down
    }

    public enum Axis
    {
        X,
        Y,
        Z
    }

    internal sealed class DirectionJsonConverter : JsonConverter<Direction?>
    {
        private static readonly System.Collections.Generic.Dictionary<string, Direction> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["north"] = Direction.North,
                ["south"] = Direction.South,
                ["west"] = Direction.West,
                ["east"] = Direction.East,
                ["up"] = Direction.Up,
                ["down"] = Direction.Down
            };
        
        public static bool TryParse(string name, out Direction direction)
        {
            if (Map.TryGetValue(name, out direction))
                return true;
            direction = default;
            return false;
        }

        public override Direction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return null;
            }

            var value = reader.GetString();
            return TryParse(value, out var direction) ? direction : null;
        }

        public override void Write(Utf8JsonWriter writer, Direction? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.Value.ToString().ToLowerInvariant());
        }
    }

    internal sealed class AxisJsonConverter : JsonConverter<Axis>
    {
        public override Axis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return Axis.Y;
            }

            var value = reader.GetString();
            return value switch
            {
                "x" => Axis.X,
                "y" => Axis.Y,
                "z" => Axis.Z,
                _ => Axis.Y
            };
        }

        public override void Write(Utf8JsonWriter writer, Axis value, JsonSerializerOptions options)
        {
            var str = value switch
            {
                Axis.X => "x",
                Axis.Y => "y",
                Axis.Z => "z",
                _ => "y"
            };
            writer.WriteStringValue(str);
        }
    }
}