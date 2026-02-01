#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace M2V.Editor.Minecraft.Model
{
    /// <summary>
    /// Represents a data structure of <code>/models/*.json</code> in the game assets.
    /// </summary>
    /// <seealso href="https://minecraft.wiki/w/Model"/>
    /// <seealso href="https://mcsrc.dev/#1/26.1-snapshot-5/net/minecraft/client/renderer/block/model/BlockModel"/>
    public sealed record BlockModel
    {
        private const int MaxTextureIndirections = 16;

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new SimpleModelSelectorsJsonConverter(),
                new MultiPartDefinitionJsonConverter(),
                new VariantListJsonConverter(),
                new ConditionJsonConverter(),
                new BlockElementFacesJsonConverter(),
                new ResourceLocationJsonConverter(),
                new Vector3JsonConverter(),
                new AxisJsonConverter()
            }
        };

        private static List<BlockElement>? ResolveElements(List<BlockElement>? childElements,
            List<BlockElement>? parentElements) =>
            childElements ?? parentElements;

        private static Dictionary<string, string> ResolveTextures(
            Dictionary<string, string>? childTextures,
            Dictionary<string, string>? parentTextures
        )
        {
            if (parentTextures == null || parentTextures.Count == 0)
            {
                return childTextures ?? new Dictionary<string, string>(StringComparer.Ordinal);
            }

            if (childTextures == null || childTextures.Count == 0)
            {
                return parentTextures;
            }

            var merged = new Dictionary<string, string>(parentTextures, StringComparer.Ordinal);
            foreach (var pair in childTextures)
            {
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        [JsonPropertyName("parent")] public ResourceLocation Parent { get; init; }
        [JsonPropertyName("textures")] public Dictionary<string, string> Textures { get; init; } = new();
        [JsonPropertyName("elements")] public List<BlockElement>? Elements { get; init; }

        public IEnumerable<ResourceLocation> GetResolvedTextures()
        {
            var seen = new HashSet<ResourceLocation>();

            foreach (var value in Textures.Values)
            {
                if (TryResolveTexture(value, out var resolved))
                    yield return resolved;
            }

            foreach (var face in (Elements ?? Enumerable.Empty<BlockElement>())
                     .Select(element => element.Faces)
                     .OfType<Dictionary<Direction, BlockElementFace>>()
                     .SelectMany(faces => faces.Values))
            {
                if (TryResolveTexture(face?.Texture, out var resolved))
                    yield return resolved;
            }

            yield break;

            bool TryResolveTexture(string? texture, out ResourceLocation resolved)
            {
                resolved = texture != null ?　ResolveTexture(texture) : ResourceLocation.Empty;
                return !resolved.IsEmpty && seen.Add(resolved);
            }
        }

        public ResourceLocation ResolveTexture(string texture)
        {
            if (string.IsNullOrEmpty(texture)) return ResourceLocation.Empty;

            var current = texture;
            var visited = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < MaxTextureIndirections && IsReference(current); i++)
            {
                if (!visited.Add(current)
                    || !Textures.TryGetValue(GetReferenceKey(current), out var resolved)
                    || string.IsNullOrEmpty(resolved))
                {
                    return ResourceLocation.Empty;
                }

                current = resolved;
            }

            return IsReference(current) ? ResourceLocation.Empty : ResourceLocation.Parse(current);

            static string GetReferenceKey(string reference) => reference[1..];

            static bool IsReference(string value) => value.Length > 0 && value.StartsWith("#");
        }

        public BlockModel WithParentApplied(BlockModel parent)
        {
            var elements = ResolveElements(Elements, parent.Elements);
            var textures = ResolveTextures(Textures, parent.Textures);

            return ReferenceEquals(textures, Textures) && ReferenceEquals(elements, Elements)
                ? this
                : this with { Textures = textures, Elements = elements };
        }

        public bool IsFullCube()
        {
            if (Elements == null || Elements.Count == 0) return false;

            foreach (var element in Elements)
            {
                if (element is not { Rotation: null })
                {
                    return false;
                }

                if (!element.IsFullCube)
                {
                    return false;
                }
            }

            return true;
        }
    }
}