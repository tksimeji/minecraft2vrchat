using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2V.Editor.Minecraft
{
    public readonly struct ResourceLocation : IEquatable<ResourceLocation>
    {
        public const string MinecraftNamespace = "minecraft";
        private const string Separator = ":";

        public static readonly ResourceLocation Empty = new(string.Empty, string.Empty);

        public static bool operator ==(ResourceLocation left, ResourceLocation right) => left.Equals(right);

        public static bool operator !=(ResourceLocation left, ResourceLocation right) => !left.Equals(right);

        public static ResourceLocation Of(string path) => new(MinecraftNamespace, path);

        public static ResourceLocation Of(string ns, string path) => new(ns, path);

        public static ResourceLocation Parse(string str, string defaultNamespace = MinecraftNamespace)
        {
            var separatorIndex = str.IndexOf(Separator, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return new ResourceLocation(defaultNamespace, str);
            }

            var ns = str[..separatorIndex];
            var path = str[(separatorIndex + 1)..];

            return new ResourceLocation(ns, path);
        }

        public string Namespace { get; }
        public string Path { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Namespace) || string.IsNullOrEmpty(Path);

        private ResourceLocation(string ns, string path)
        {
            Namespace = ns ?? string.Empty;
            Path = path ?? string.Empty;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (Namespace?.GetHashCode() ?? 0);
                hash = hash * 31 + (Path?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public string ToAssetPath() => IsEmpty ? string.Empty : $"assets/{Namespace}/textures/{Path}.png";

        public override string ToString() => IsEmpty ? string.Empty : $"{Namespace}:{Path}";

        public override bool Equals(object obj)
        {
            return obj is ResourceLocation other && Equals(other);
        }

        public bool Equals(ResourceLocation other)
        {
            return string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
                   string.Equals(Path, other.Path, StringComparison.Ordinal);
        }
    }

    internal sealed class ResourceLocationJsonConverter : JsonConverter<ResourceLocation>
    {
        public override ResourceLocation Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                reader.Skip();
                return ResourceLocation.Empty;
            }

            var value = reader.GetString();
            return ResourceLocation.Parse(value);
        }

        public override void Write(Utf8JsonWriter writer, ResourceLocation value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}