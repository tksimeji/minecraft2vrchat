#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using fNbt;

namespace M2V.Editor.Minecraft.Model
{
    /// <summary>
    /// Represents a data structure of <code>/blockstates/*.json</code> in the game assets.
    /// </summary>
    /// <seealso href="https://minecraft.wiki/w/Blockstates_definition"/>
    /// <seealso href="https://mcsrc.dev/#1/26.1-snapshot-5/net/minecraft/client/renderer/block/model/BlockModelDefinition"/>
    public sealed record BlockModelDefinition
    {
        [JsonPropertyName("variants")] public SimpleModelSelectors? SimpleModels { get; init; }
        [JsonPropertyName("multipart")] public MultiPartDefinition? MultiPart { get; init; }

        internal List<Variant> GetResolvedVariants(IReadOnlyDictionary<string, NbtTag> properties)
        {
            var result = new List<Variant>();

            var bestMatch = SimpleModels?.FindBestMatch(properties);
            if (bestMatch != null)
            {
                AddChosenVariant(result, bestMatch);
            }

            if (MultiPart == null) return result;
            
            foreach (var selector in MultiPart.Selectors.Where(selector => selector.MatchesProperties(properties)))
            {
                AddChosenVariant(result, selector.Variant);
            }

            return result;
        }

        private static void AddChosenVariant(List<Variant> target, List<Variant> variants)
        {
            var chosen = ChooseVariantByWeight(variants);
            if (chosen == null) return;
            target.Add(chosen);
        }

        private static Variant? ChooseVariantByWeight(List<Variant> variants)
        {
            switch (variants.Count)
            {
                case 0:
                    return null;
                case 1:
                    return variants[0];
            }

            var totalWeight = variants.Sum(variant => variant.Weight ?? 1);

            if (totalWeight <= 0) return variants[0];

            var roll = UnityEngine.Random.Range(0, totalWeight);
            
            foreach (var variant in variants)
            {
                roll -= variant.Weight ?? 1;
                if (roll < 0)
                {
                    return variant;
                }
            }

            return variants[0];
        }
    }
}